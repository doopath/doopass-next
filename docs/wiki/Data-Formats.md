# Data Formats

All formats are inherited from the original Python implementation. Treat them
as frozen: existing user files must keep opening.

## 1. The vault file (`store.enc`)

### 1.1 Outer container

The file is **text**: a single URL-safe base64 string (padding included, no
trailing newline, UTF-8 without BOM).

```
base64url( salt[16] || iterations[4] || fernet_token[n] )
```

| Field | Size | Meaning |
| ----- | ---- | ------- |
| `salt` | 16 bytes | random per write, `RandomNumberGenerator.GetBytes(16)` |
| `iterations` | 4 bytes | **big-endian int32**, PBKDF2 iteration count (default 100 000) |
| `fernet_token` | variable | raw bytes of a standard Fernet token (already base64url-encoded inside the container) |

Base64url encoding is Python-compatible: `+`→`-`, `/`→`_`, `=` padding kept on
encode; the decoder also accepts unpadded input. Implemented in
`Doopass.Core.Crypto.Base64Url`.

Because the iteration count travels with each file, raising the default later
does not break old files.

### 1.2 Fernet token

```
        0x80 | timestamp[8] | IV[16] | ciphertext[16k] | HMAC-SHA256[32]
        ver    big-endian     random   AES-128-CBC,     over everything
               unix seconds            PKCS#7 padded    before the MAC
```

Key derivation (`Doopass.Core.Crypto.Pbkdf2`):

```
key32 = PBKDF2-HMAC-SHA256(UTF8(password), salt, iterations, dkLen = 32)
signing_key    = key32[0..16]
encryption_key = key32[16..32]
```

* MAC is `HMAC-SHA256(signing_key, version||timestamp||IV||ciphertext)`.
* Verification uses `CryptographicOperations.FixedTimeEquals`.
* The timestamp is authenticated but **not** validated against any TTL; Fernet
  expiry semantics of the Python library are not reproduced.
* A malformed container, a bad version byte, a MAC mismatch or a padding error
  all surface as `IncorrectPasswordError` — deliberately indistinguishable.

### 1.3 Plaintext layout

The decrypted payload is a UTF-8 string of lines:

```
github = hunter2
router = admin:pa$$=word
empty_key =
```

| Rule | Details |
| ---- | ------- |
| Separator | first `=` in the line |
| Key | text before the first `=`, trimmed |
| Value | text after it, trimmed |
| Line ending | `\n` between entries (`string.Join("\n", ...)`), no trailing newline |
| Blank lines | skipped on load |
| Duplicates | not expected; on load both are kept, lookups resolve to the first, and saving rewrites both |
| Escaping | none — no quoting, no multi-line values |

This is exactly `"\n".join(f"{k} = {v}" for ...)` on the Python side.

### 1.4 Write behaviour

Every mutation (`AddProperty`, `SetValue`, `RemoveProperty`) serialises all
entries, encrypts with a **fresh salt and IV**, and overwrites the file in place
with `File.WriteAllText(..., Utf8NoBom)`. There is no temp-file swap, no fsync
and no backup on save (backups are explicit).

## 2. Settings file (`settings.json`)

Location: `<AppDataDir>/settings.json`, default `~/doopass/appdata/settings.json`.

```json
{
  "local": {
    "version": "5.0.0",
    "backup_extension": ".enc.bak",
    "store_file": "APPDATA/store.enc",
    "log_file": "APPDATA/doopass.log",
    "backups": "APPDATA/backups",
    "default_config": "APPDATA/default_config.json"
  }
}
```

| Rule | Details |
| ---- | ------- |
| Shape | `Dictionary<string, Dictionary<string, string>>`; all values are JSON strings |
| Indentation | `WriteIndented = true` (2 spaces), UTF-8 no BOM |
| `APPDATA/` placeholder | prefix replaced with the real `AppDataDir` on load; re-applied on save when the value starts with that directory |
| Obsolete section | `remote` is dropped on load and the file is rewritten |
| Extra sections | preserved (`api` is the documented one: `tls_pfx`, `tls_pass`, `session_ttl_minutes`, `announce`) |
| Missing file | the embedded default (`Doopass.Core.Configuration.DefaultConfig.json`) is written |
| BOM on input | tolerated (`File.ReadAllText` + `JsonSerializer`) |

The defaults are compiled into `Doopass.Core` as an embedded resource named
`Doopass.Core.Configuration.DefaultConfig.json`.

## 3. Backups

Directory: `settings.local.backups`, default `~/doopass/appdata/backups`.

```
<dd_MM_yyyy>_<storeBase>_<n><extension>

26_10_2026_store_1.enc.bak
26_10_2026_store_2.enc.bak
27_10_2026_store_1.enc.bak
```

| Part | Rule |
| ---- | ---- |
| `dd_MM_yyyy` | `DateTime.Today.ToString("dd_MM_yyyy")`, local time |
| `storeBase` | store file name without its `.enc` suffix (`store` for `store.enc`) |
| `n` | next integer after the highest existing number for that day, starting at 1 |
| `extension` | `settings.local.backup_extension`, default `.enc.bak` |

Content is a byte-for-byte copy of `store.enc` (still encrypted). Listing strips
the extension and sorts names **ordinal descending**.

## 4. Log file

Location: `settings.local.log_file`, default `~/doopass/appdata/doopass.log`.
Rotation: at 5 MB the file is moved to `<file>.1` (the previous `.1` is
deleted).

```
2026-10-29 14:03:11,908 INFO Store opened: /home/user/doopass/appdata/store.enc (12 entries)
2026-10-29 14:03:44,102 INFO Property added: github
2026-10-29 14:04:02,554 ERROR Backup creation failed: System.IO.IOException: ...
```

Format: `yyyy-MM-dd HH:mm:ss,fff LEVEL message`, one line per event, UTF-8 no
BOM. Levels: `INFO`, `DEBUG`, `ERROR`.

Recorded events include: store initialised/opened, property added/updated/
removed (**key names only**), backup created/deleted, store restored, settings
change (`section.key`), settings restored, obsolete section dropped, TLS
fingerprint, web server start/stop, discovery start, discovery failures.

Never recorded: passwords, secret values, API tokens, certificate passwords.

## 5. REST API wire format

Base content type: `application/json`; responses carry
`Cache-Control: no-store` for the whole `/api` group.

### Request/response bodies

| Endpoint | Body in | Body out |
| -------- | ------- | -------- |
| `POST /api/auth/login` | `{"password": "..."}` | `{"token": "...", "serverVersion": "5.0.0"}` |
| `POST /api/auth/init` | `{"password": "..."}` | same |
| `GET /api/vault/state` | — | `{"storeExists":true,"unlocked":false,"serverVersion":"5.0.0","certFingerprint":null}` |
| `GET /api/vault/keys` | — | `{"keys":["github","router"]}` |
| `GET /api/vault/value?key=` | — | `{"value":"..."}` |
| `POST /api/vault/pairs` | `{"key":"k","value":"v"}` | 204 |
| `PUT /api/vault/pairs` | `{"oldKey":"a","newKey":"b","newValue":"v"}` | 204 |
| `DELETE /api/vault/pairs?key=` | — | 204 |
| `GET /api/backups` | — | `{"names":["26_10_2026_store_1"]}` |
| `POST /api/backups/restore?name=` | — | 204 |
| `DELETE /api/backups?name=` | — | 204 |
| `GET /api/settings` | — | `{"sections":[{"name":"local","pairs":{...}}]}` |
| `PUT /api/settings/{section}/{key}` | `{"value":"..."}` | 204 |
| `POST /api/settings/restore-defaults` | — | 204 |
| any error | — | `{"error":"message"}` |

JSON property names are camelCase (`JsonSerializerDefaults.Web` on the client,
anonymous objects / default camelCase policy on the server).

### Authentication header

```
Authorization: Bearer <token>
```

Parsed with a strict regex: `^Bearer[ ]+[A-Za-z0-9_-]+$`. Anything else is
treated as "no token" → 401 with the JSON error body.

### SignalR hub

URL `/hubs/vault`, token via the standard `access_token` query parameter.
Server-to-client methods (no arguments):

| Method | Meaning |
| ------ | ------- |
| `vaultChanged` | pairs added/updated/removed, or a restore happened |
| `backupsChanged` | a snapshot was created, restored or deleted |
| `settingsChanged` | settings edited or restored to defaults |
| `vaultLocked` | the caller's session ended, or the whole vault relocked |

Invalid/expired token on connect: the connection is aborted.

## 6. LAN discovery datagrams

Transport: UDP, port **7241**, IPv4 broadcast (global and directed).

| Direction | Payload |
| --------- | ------- |
| probe (client → host) | the 16 ASCII bytes `DOOPASS_DISCOVER` |
| reply (host → client) | UTF-8 JSON, camelCase |

Reply:

```json
{
  "service": "doopass",
  "name": "DESKTOP-7F3",
  "port": 7240,
  "tls": true,
  "fingerprint": "d3894b6e...c8e50f",
  "version": "5.0.0"
}
```

The host only answers a datagram whose bytes match the probe **exactly**; the
client only accepts replies with `service == "doopass"` and a port in
`1..65535`, and derives the address from the UDP sender (not from the payload).

## 7. Browser storage

| Key | Store | Value |
| --- | ----- | ----- |
| `doopass-theme` | `localStorage` | `"dark"` or `"light"`; anything else is ignored |
| Blazor circuit | server memory | session state, no browser cookie |

## 8. Android storage

| Data | Location |
| ---- | -------- |
| Host URL, certificate pin, session token | `SecureStorage` keys `doopass.host`, `doopass.pin`, `doopass.token` (Keystore-backed) |
| Log | `FileSystem.AppDataDirectory/doopass.log` |
| No vault, no settings, no backups | — |

## 9. Environment variables and paths

| Variable | Effect |
| -------- | ------ |
| `DOOPASS_HOME` | overrides the application root (default `~/doopass`) |
| `HOME` | fallback for the home directory when the platform does not report one |

```
<root>/
└── appdata/
    ├── store.enc
    ├── settings.json
    ├── backups/
    └── doopass.log (+ doopass.log.1)
```
