# Security Model

This page describes what Doopass protects, how, and — just as importantly —
what it does **not** protect. It is written from the code, not from marketing.

## 1. Assets

| Asset | Where it lives | Protection |
| ----- | -------------- | ---------- |
| Secret values and key names | `store.enc` | Fernet (AES-128-CBC + HMAC-SHA256) with a PBKDF2-derived key |
| Master password | process memory while unlocked | none (see §6) |
| API session tokens | host memory | random 256-bit, TTL, never written to disk or logs |
| TLS private key | user-supplied `.pfx` path | filesystem permissions |
| Certificate pin | Android `SecureStorage` | Keystore-backed |
| Phone session token | Android `SecureStorage` | Keystore-backed |

## 2. Cryptography

| Primitive | Parameters | Implementation |
| --------- | ---------- | -------------- |
| Key derivation | PBKDF2-HMAC-SHA256, 100 000 iterations (default), 16-byte random salt per write, 32-byte output | `Pbkdf2`, `Store.Encrypt` |
| Confidentiality | AES-128-CBC, PKCS#7, 16-byte random IV per token | `Fernet.Aes` |
| Integrity | HMAC-SHA256 over version‖timestamp‖IV‖ciphertext, 16-byte signing key | `Fernet` |
| Comparison | constant-time `CryptographicOperations.FixedTimeEquals` | `Fernet.Decrypt` |
| Tokens | 32 bytes from `RandomNumberGenerator`, base64url | `ApiVaultCoordinator.NewToken` |
| TLS fingerprint | SHA-256 over the DER certificate, lowercase hex | `WebHostFactory.ValidateTlsCertificate`, `RemoteVaultSession` |

The construction is the standard Fernet spec with the Python argument order
(signing key first, encryption key second), so tokens are interchangeable with
`cryptography.fernet.Fernet`.

### Deliberate properties

* **Fresh salt and IV on every save.** Two saves of identical content produce
  different ciphertexts.
* **Authenticated before decryption.** A modified file never reaches the AES
  decryptor.
* **One error for all failures.** Wrong password, truncated file, corrupted MAC
  and bad padding all raise `IncorrectPasswordError`, so an attacker learns
  nothing beyond "this password does not open this file".
* **Iteration count per file.** Stored in the container, allowing a future
  increase without breaking old vaults.

### Known weaknesses of the scheme (inherited)

* PBKDF2-SHA256 with 100 000 iterations is weak against GPU/ASIC cracking by
  current standards. It is kept for Python compatibility.
* No key separation beyond the Fernet key split; no per-entry encryption.
* No authenticated metadata: the iteration count is *not* covered by the MAC
  (it is read before the key is derived), so an attacker who can modify the file
  can lower it and force an expensive-but-cheaper guess; the resulting key still
  has to pass the HMAC check.
* The master password is stored as a .NET `string`; it cannot be zeroed
  deterministically.

## 3. Threat model

### Protected against

| Threat | Mitigation |
| ------ | ---------- |
| Stolen backup / cloud-synced folder / disk image | Everything is encrypted with the master password |
| Casual file inspection | No plaintext, no key names, no metadata in `store.enc`; log contains keys only |
| Network eavesdropper on a TLS host | HTTPS with a user-supplied certificate; the phone pins its SHA-256 |
| Host impersonation on the LAN | Pin mismatch fails the handshake before any password or secret is sent |
| Brute force of the REST login | Fixed-window limiter: 5 attempts/minute per remote IP, `429` afterwards |
| Session replay after logout | Logout removes the token server-side; expiry re-checks every call |
| Accidental secret leakage into logs | Logger call sites record names, counts and paths, never values (asserted by `AppLoggerTests`) |

### Not protected against

| Threat | Why / consequence |
| ------ | ----------------- |
| Malware or another user with access to the unlocked process | The decrypted vault and password live in memory; `ptrace`/debugger access wins |
| An attacker who can read the process memory of the host | Same as above; no memory encryption |
| Offline brute force with a weak master password | PBKDF2 slows but does not prevent guessing; there is no password strength policy |
| Plain HTTP on the LAN (`--lan` without `--tls`) | Bearer tokens and secret values travel in clear text; anyone on the network can read or replay them |
| Someone who can reach the web port | The UI has **no login rate limiting**; only the REST endpoints are limited |
| A hostile browser extension or a compromised browser | The web UI renders secrets into the DOM when revealed and writes them to the clipboard |
| Clipboard snooping | Copied secrets stay in the OS clipboard until replaced |
| A stolen phone with an unlocked session | The token in `SecureStorage` grants API access until TTL expiry or logout |
| Denial of service | No quotas; large values or many entries can exhaust memory/disk |
| Supply-chain attacks | Dependencies are NuGet packages and the .NET SDK; no SBOM/signing step |

## 4. Trust boundaries

```
   [ Android phone ] --TLS(or HTTP)--> [ host process ] --> [ store.enc ]
        trusts:                          trusts:
        - pinned cert (if https)          - the local user / filesystem
        - the host it paired with         - settings.json contents
                                          - the .pfx it is told to load
```

* **The host is the security boundary.** Any client the host accepts can read
  every secret the vault holds. There is no per-entry access control.
* **The vault file is trusted input** only after MAC verification; the settings
  file and CLI arguments are trusted implicitly (they are local configuration).
* **The phone trusts the pin, not a CA.** A self-signed certificate is
  sufficient; a public CA is irrelevant for pinning.
* **Browsers do not pin.** A self-signed certificate only produces a warning,
  and blocks PWA installation (which requires a secure, trusted origin).

## 5. Authentication and authorization

| Surface | Mechanism | Notes |
| ------- | --------- | ----- |
| TUI / web UI | master password unlocks the vault in that session | no accounts, no roles |
| REST API | opaque bearer token issued after a password check | token equals authorization: full vault access |
| SignalR hub | token as `access_token` query parameter | aborted if unknown/expired |
| `GET /api/vault/state` | anonymous | leaks existence, version, TLS fingerprint |
| LAN discovery | anonymous | leaks host name, port, version, fingerprint to the LAN |

The API has no cookies, so there is no CSRF surface; the browser UI uses
ASP.NET Core antiforgery middleware. All API responses are `no-store` so
secret-bearing bodies are not cached by proxies or browsers.

## 6. Memory and lifetime rules

* The decrypted entry list and the password exist only inside a live `Store`
  instance.
* Web UI: dropped on logout, circuit disposal or server shutdown → password
  forgotten, must be re-entered.
* REST: the single shared store is dropped when the last token disappears
  (logout/TTL). Until then, any new login only adds a token.
* Phone: the master password is **never stored**; only the resulting token.
* Nothing is written to swap by the application deliberately, but the OS may
  swap process memory — full-disk encryption is the mitigation.

## 7. Transport security

| Setup | Outcome |
| ----- | ------- |
| `http://localhost:7240` | loopback only; acceptable for a single-user desktop |
| `--lan` (HTTP, all interfaces) | usable but unsafe on shared networks; UI shows a warning on the phone |
| `--tls host.pfx --tls-pass ...` | HTTPS on all interfaces; fingerprint printed at startup and reported by the API |
| Self-signed certificate | fine for the Android client (pinning), rejected/warned by browsers; PWA install blocked |
| Public CA certificate | best browser experience; pinning still works on the phone |

The TLS private key and password are user-managed. `api.tls_pass` in
`settings.json` is stored **in plain text** — that is documented behaviour, not
an oversight.

## 8. What is never logged

Passwords, secret values, API tokens, certificate passwords, request bodies.
The rule is enforced by convention at every call site and covered by
`AppLoggerTests.StoreOperations_AreLogged_WithoutSecrets`, which opens a store,
performs CRUD and asserts that neither the password nor the secret value appears
in the log file. Key **names** are logged.

## 9. Hardening checklist

For a deployment on an untrusted network:

1. Create a certificate whose SAN includes the LAN IP and run `serve --tls`.
2. Confirm the fingerprint on the phone and keep the pin (re-pair after
   rotation).
3. Firewall: allow only TCP 7240 and UDP 7241 from the LAN you trust.
4. Set `api.session_ttl_minutes` to a short value if a phone might be lost.
5. Use a long, unique master password — it is the only barrier between a copied
   `store.enc` and the secrets.
6. Do not commit `doopass.pfx`, `doopass.key` or `store.enc`; keep backups on
   encrypted media.
7. Log out (or stop the host) when leaving the machine unattended.
