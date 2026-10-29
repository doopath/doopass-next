# Glossary

Terms used across the code base, the API and this wiki.

## Domain

**Vault / store** — the encrypted file (`store.enc`) plus its decrypted
in-memory representation. One vault per installation.

**Pair / entry / property** — one `key = value` secret. The only data structure
the product stores. Code uses "property" (`AddProperty`, `PropertyDoesNotExistError`);
UI copy says "pair" or "item".

**Key** — the name of a secret (case-sensitive, arbitrary string).

**Value** — the secret itself (arbitrary string, trimmed on load).

**Master password** — the password that derives the vault key. Never stored;
required for every unlock (and again for every new web circuit).

**Snapshot / backup** — a byte copy of `store.enc` in `appdata/backups`, named
`dd_MM_yyyy_<store>_<n>.enc.bak`. Still encrypted.

**Safety backup** — the automatic snapshot taken *before* a restore, so every
restore can be undone.

## Cryptography

**Fernet** — the token format used for the vault (version byte, timestamp, IV,
AES-128-CBC ciphertext, HMAC-SHA256).

**PBKDF2** — password-based key derivation; here HMAC-SHA256 with 100 000
iterations and a random 16-byte salt.

**Container** — the outer `store.enc` encoding:
`base64url(salt ‖ iterations ‖ fernet_token)`.

**Iteration count** — stored in the container so a future default can increase
without breaking existing files.

**Fingerprint** — lowercase hex SHA-256 of a TLS certificate's DER bytes; what
the phone pins.

**Pin / trust-on-first-use** — storing the host fingerprint on first connection
and requiring an exact match afterwards.

## Architecture

**Host** — a running `doopass serve` process that owns the vault and exposes the
web UI, REST API, hub and discovery.

**Thin client** — the Android app: no local vault, only host address, pin and
token.

**Front-end** — one of TUI, web UI, Android UI. All sit on the same Core layer.

**Circuit** — a Blazor Server connection; the unit of web-UI session state.

**Scope (DI)** — per-circuit service lifetime in the web host; singleton means
host-wide.

**RCL** — Razor Class Library; `Doopass.WebUi`, shared between the host and the
phone.

**Hidden/embedded asset** — a CSS/JS/icon file compiled into an assembly and
served from a mapped route, so a single-file publish needs no loose files.

## Sessions and sync

**Unlocked** — the decrypted vault is in memory for that session.

**Lock** — dropping that in-memory copy (logout, TTL expiry, `G` in the TUI,
server shutdown). The file is untouched.

**Session token** — opaque 32-byte base64url string issued by the REST API.

**Sliding TTL** — the token stays valid while used within the window (default 30
minutes); every authorized request refreshes it.

**Relock** — automatic lock when the last API session disappears.

**Change event** — `vaultChanged` / `backupsChanged` / `settingsChanged` /
`vaultLocked`, delivered over SignalR and internally through
`IVaultChangeNotifier` / `IVaultEvents`.

**Relay** — the per-scope `VaultEventRelay` that turns server or hub events into
component events.

## Network

**Discovery** — UDP broadcast probe (`DOOPASS_DISCOVER`) on port 7241 answered
with a JSON announcement.

**Directed broadcast** — the subnet broadcast address (e.g. `192.168.1.255`),
used in addition to `255.255.255.255` because some Wi-Fi stacks drop the latter.

**Hub** — the SignalR endpoint `/hubs/vault`.

**Bearer token** — `Authorization: Bearer <token>`; the only REST auth scheme.

**No-store** — `Cache-Control: no-store`, set on every API response.

## UI

**TUI** — the Terminal.Gui interface; screens are stacked in `DoopassWindow`.

**Global key route** — `DoopassWindow.RouteKey`, which sees keys before any
focused view.

**Tab stop** — a focusable control registered in a screen's explicit order.

**Auth layout** — the centred card used by login and init in the web UI.

**Toast** — a transient success/error/info message (auto-dismissed after 3.8 s).

**PWA** — the installable web app (manifest + service worker + icons).

**Standalone mode** — an installed PWA running without browser chrome.

**APPDATA placeholder** — the portable path prefix used in `settings.json`,
expanded to the real data directory on load.

## Operations

**DOOPASS_HOME** — environment variable overriding the application root
(default `~/doopass`).

**appdata** — the directory holding the vault, settings, backups and log.

**Single-file publish** — self-contained build producing one executable with the
runtime and embedded assets.
