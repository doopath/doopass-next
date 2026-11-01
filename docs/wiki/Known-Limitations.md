# Known Limitations

Deliberate trade-offs and genuine sharp edges, documented so they are not
rediscovered the hard way. Security-relevant items are also summarised in
[Security Model](Security-Model.md).

## Rename can lose the old key

**What:** "update" is implemented as `RemoveProperty(old)` + `AddProperty(new)`.

* The **TUI** checks whether `new` already exists *before* removing `old`.
* The **browser UI** and the **REST API** do not. If the new key exists, the old
  key is already gone and the add fails with a 409, leaving the entry deleted.

**Mitigation:** rename through the TUI, or delete the target key first. A safety
backup taken beforehand is the only recovery.

## Backup ordering is lexicographic

`GetBackupNames()` sorts file names ordinal-descending. Within one day that is
newest-first, but `29_10_2026_...` sorts before `01_11_2026_...`, so the list is
not strictly chronological across months. The UI shows names as stored and does
not parse dates.

## Cryptography and password policy

* PBKDF2-HMAC-SHA256 with 100 000 iterations is weak against modern GPU
  cracking; it is kept for Python compatibility.
* No password strength requirement beyond "non-empty, no edge spaces".
* The iteration count is read before key derivation and is not covered by the
  MAC.
* The master password lives in a .NET `string`: it can be dropped but not zeroed.
* No key rotation: changing the master password is not supported at all.

## Plain HTTP mode

`--lan` without `--tls` exposes bearer tokens and secret values in clear text.
Discovery also announces the host on the network. There is no built-in
authentication for the browser UI beyond the master password.

## Discovery is unauthenticated

Anyone on the LAN can learn the host name, port, version and TLS fingerprint, and
can query `/api/vault/state` for `storeExists`/`unlocked`. The UDP port (7241) is
fixed and cannot be configured; two hosts on one machine cannot both announce.

## Storage format constraints

* Values are trimmed on load: leading/trailing whitespace does not survive.
* No escaping, no multi-line values, no binary data, no per-entry metadata.
* Keys are case-sensitive and unbounded in length; extremely long values are held
  in memory in full and copied to the clipboard in full.
* Duplicate keys on disk are tolerated on read (first wins) but never merged.

## Missing product features

No password generator, no TOTP/2FA storage, no attachments, no browser
extension, no autofill, no import from other managers, no multi-vault support, no
sharing, no folders/tags, no search by value.

## No replay of change events

SignalR events carry no payload and are not queued. A client that was offline
misses them and only re-syncs when it next loads a page or performs an action.
Changes made outside the app (editing the file, running the TUI, a second host)
are not detected at all — `/api/vault/reload` is the manual escape hatch.

## Sessions

* A hub connection does not refresh the sliding TTL; only REST calls do. A phone
  or browser tab that sits idle on the store page expires after the TTL.
* The browser token lives in `sessionStorage`, so it survives a page reload and
  is readable by any script on the origin; closing the tab does not itself drop
  the host's store, the TTL/janitor does.
* API tokens are all-powerful: any token reads and writes the whole vault.
* There is no "log out all devices" action.

## Phone specifics

* No automated tests; the app is verified manually.
* AOT and trimming are disabled, so the APK is larger and slower to start.
* Minimum Android 6.0 (API 23).
* No background sync or push notifications; the app must be open to receive live
  events.
* The emulator cannot discover hosts (use `10.0.2.2`).
* Certificate pinning checks only the fingerprint — not host name, SAN, chain or
  expiry.

## PWA specifics

* Installation requires `http://localhost` or a browser-trusted HTTPS origin;
  a self-signed certificate is enough for the phone but blocks installation.
* The service worker only provides a shell: vault operations need the server.
* Asset URLs are unhashed; after an upgrade the worker cache version
  (`doopass-pwa-v9`) must be bumped, otherwise an installed app can serve stale
  CSS/JS.

## Settings

* Every value is a string; no schema validation, no typed settings UI.
* "Restore defaults" deletes the whole `[api]` section, including TLS paths.
* `api.tls_pass` is stored in plain text.
* Editing a missing `[local]` key leaves accessors throwing
  `KeyNotFoundException` at startup.

## Operations and tooling

* The version is duplicated in `Constants.cs`, seven `.csproj` files and
  `DefaultConfig.json`; nothing enforces consistency.
* No coverage reporting, no container image, no package signing, no SBOM. CI
  builds and tests on push, but there is no release pipeline.
* No structured logging, no log levels, one 5 MB archive only.
* Logging from multiple processes to the same file can interleave.
* Rate limiting keys on `RemoteIpAddress`, so behind a reverse proxy all clients
  share one bucket.

## Data exposure in the client

* Every secret the browser shows crosses the Network tab in a plaintext JSON
  response — unavoidable unless decryption moves into the browser (not planned).
* The bearer token in `sessionStorage` is readable by any same-origin script;
  the CSP limits what can load, but a hostile extension or DevTools still sees it.
* Revealed values are rendered into the DOM and can be read by extensions or
  DevTools; the app cannot prevent that.
* Copied secrets stay in the OS clipboard until replaced.
* No auto-hide timer for revealed values; they stay visible until the user hides
  them or the page is left.
