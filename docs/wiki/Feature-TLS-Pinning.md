# Feature: TLS and Certificate Pinning

The host can serve HTTPS with a user-supplied certificate. The Android client
**pins** that certificate's SHA-256 fingerprint, so a self-signed certificate is
enough for the phone — no CA, no domain, no reverse proxy.

## Host side

```bash
doopass serve --lan --tls ~/doopass/doopass.pfx --tls-pass CHANGE-ME
```

* `--tls <pfx>` (alias `--tls-pfx`) points at a PKCS#12 file; `--tls-pass` is its
  password. Command-line values override `settings.json` → `api.tls_pfx` /
  `api.tls_pass`.
* The certificate is loaded **before** the server starts; a missing or unloadable
  file aborts startup with `InvalidOperationException` (fail fast rather than
  silently serving HTTP).
* TLS mode binds **all interfaces** (remote clients are the point). Plain mode
  binds `localhost`, or `0.0.0.0` with `--lan`.
* At startup the log prints the fingerprint in the compact lowercase-hex form the
  phone pins:

```
TLS certificate SHA-256 fingerprint: d3894b6e…c8e50f
```

* `GET /api/vault/state` returns the same value as `certFingerprint`, so clients
  can display it. Discovery replies include it as well.

### Creating a self-signed certificate

```bash
cd ~/doopass
LAN_IP=192.168.1.10
HOST=$(hostname)
openssl req -x509 -newkey rsa:2048 -sha256 -days 825 -nodes \
  -keyout doopass.key -out doopass.crt \
  -subj "/CN=$HOST" \
  -addext "subjectAltName=DNS:$HOST,DNS:localhost,IP:$LAN_IP,IP:127.0.0.1"
openssl pkcs12 -export -out doopass.pfx \
  -inkey doopass.key -in doopass.crt -passout pass:CHANGE-ME
```

PowerShell equivalent:

```powershell
$cert = New-SelfSignedCertificate -Subject 'CN=doopass' `
    -TextExtension @('2.5.29.17={text}DNS=doopass&DNS=localhost&IPAddress=192.168.1.10&IPAddress=127.0.0.1') `
    -NotAfter (Get-Date).AddDays(825)
Export-PfxCertificate -Cert $cert -FilePath doopass.pfx `
    -Password (ConvertTo-SecureString 'CHANGE-ME' -AsPlainText -Force)
```

Keep `doopass.key` secret and never commit `.pfx`/`.key`.

## Client side (Android)

### First connection — trust on first use

1. The connect screen probes `GET /api/vault/state`, **accepting any
   certificate** (there is no pin yet) and reading the certificate presented in
   the handshake. The handshake fingerprint wins over the JSON field, so a
   spoofed response body cannot choose the pin.
2. The confirmation card shows the host, the **Encrypted · TLS** badge and the
   fingerprint.
3. "Trust and connect" stores host + pin in `SecureStorage` and clears any old
   token.
4. Compare the fingerprint with the host log (or with
   `openssl x509 -in doopass.crt -noout -fingerprint -sha256`, dropping colons
   and lowercasing).

### Later connections

`RemoteVaultSession.BuildClient` installs a
`ServerCertificateCustomValidationCallback` that compares
`SHA256(certificate.RawData)` in lowercase hex with the stored pin
(case-insensitive). A mismatch fails the handshake before any request — and
therefore before any credential is sent.

What pinning does **not** check: host name, SAN, chain or expiry. The same
certificate keeps working when DHCP changes the PC's address, which browsers
would reject.

### Rotation

Regenerate the certificate, open **Change host** on the phone, pick the host
again and confirm the new fingerprint. Continuing without re-pairing is the
intended failure mode: the app refuses the host instead of trusting it silently.

### Plain HTTP

Allowed for convenience, with a permanent warning in the UI. No pin is stored.
Tokens and secrets travel in clear text on the LAN.

## Browsers vs pinning

| Client | Self-signed certificate | Public CA certificate |
| ------ | ----------------------- | --------------------- |
| Android app | works (pinned) | works |
| Browser UI | warning page, PWA install blocked | clean, PWA installable |
| PWA (installed) | cannot install over an untrusted cert | works |

`http://localhost` is also a secure origin, so a loopback-only setup can install
the PWA without TLS.

## Where the code lives

| Concern | File |
| ------- | ---- |
| Certificate loading + fingerprint | `Cli/Api/WebHostFactory.cs` (`ValidateTlsCertificate`) |
| Fingerprint in state/discovery | `Cli/Api/VaultEndpoints.cs`, `Cli/Api/DiscoveryService.cs` |
| Pinning callback | `Mobile/Services/RemoteVaultSession.cs` |
| Probe that captures the certificate | `Mobile/Components/Connect.razor` (`ProbeStateAsync`) |
| Pin storage | `Mobile/Services/ConnectionStore.cs` |

## Operational notes

* `api.tls_pass` is stored in `settings.json` in plain text; prefer the CLI flag
  or a protected file if that matters to you.
* A host restarted with a **different** certificate is unreachable for a paired
  phone until it is re-paired (by design).
* Firewall still needs TCP 7240 (or your `--port`) and UDP 7241 for discovery.
* Only one certificate per host process; there is no SNI/multi-cert support.
