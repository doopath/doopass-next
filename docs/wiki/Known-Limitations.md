# Known Limitations

Deliberate trade-offs and genuine sharp edges, documented so they are not
rediscovered the hard way. Security-relevant items are also summarised in
[Security Model](Security-Model.md).

## Two in-memory store instances

**What:** the web host can hold two independently decrypted `Store` objects at
once — one per Blazor circuit (`DoopassSession`) and one inside
`ApiVaultCoordinator` for all REST tokens.

**Consequence:** both write the whole `store.enc` on every mutation. Any client
converges on change events, so a UI sees the other's writes, but two simultaneous
writes are resolved by last-writer-wins at file level. A write based on a stale
copy can silently drop the other client's entry.

**Mitigation:** use one client type at a time for edits, take backups before bulk
changes. A real fix means a single shared store for both paths (or per-write
merge/reload), which is not implemented.

## Rename can lose the old key

**What:** "update" is implemented as `RemoveProperty(old)` + `AddProperty(new)`.

* The **TUI** checks whether `new` already exists *before* removing `old`.
* The **web session** and the **REST API** do not. If the new key exists, the old
  key is already gone and the add fails with a 409, leaving the entry deleted.

**Mitigation:** rename through the TUI, or delete the target key first. A safety
backup taken beforehand is the only recovery.

## Writes are not atomic

**What:** every save does `File.WriteAllText(store.enc, …)` in place. No temp file
+ rename, no fsync, no file lock.

**Consequence:** a crash, power loss or full disk during a save can truncate the
vault, which is then indistinguishable from a wrong password. Two processes
sharing a directory can overwrite each other.

**Mitigation:** take backups before risky changes; keep only one writer process.

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

## Web login is not rate limited

Only `/api/auth/login` and `/api/auth/init` are limited (5/min per IP). The
Blazor login form (`/`) performs an unlimited number of password attempts against
the local file, so anyone who can reach the web port can brute-force it. On a
loopback-only host this is a local attack; with `--lan` it is a network one.

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
  that sits idle on the store page expires after the TTL.
* A web circuit has no idle timeout: it stays unlocked until logout, disconnect
  or server restart.
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
  (`doopass-pwa-v2`) must be bumped, otherwise an installed app can serve stale
  CSS/JS.

## Settings

* Every value is a string; no schema validation, no typed settings UI.
* "Restore defaults" deletes the whole `[api]` section, including TLS paths.
* `api.tls_pass` is stored in plain text.
* Editing a missing `[local]` key leaves accessors throwing
  `KeyNotFoundException` at startup.

## Operations and tooling

* Version `5.0.0` is duplicated in `Constants.cs`, six `.csproj` files and
  `DefaultConfig.json`; nothing enforces consistency.
* No CI pipeline, no coverage reporting, no container image, no package signing,
  no SBOM.
* No structured logging, no log levels, one 5 MB archive only.
* Logging from multiple processes to the same file can interleave.
* Rate limiting keys on `RemoteIpAddress`, so behind a reverse proxy all clients
  share one bucket.

## Data exposure in the client

* Revealed values are rendered into the DOM and can be read by extensions or
  DevTools; the app cannot prevent that.
* Copied secrets stay in the OS clipboard until replaced.
* No auto-hide timer for revealed values; they stay visible until the user hides
  them or the page is left.
