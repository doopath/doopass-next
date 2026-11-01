# Changelog

Notable changes to Doopass. The on-disk format — `store.enc`, `settings.json`
and backup file names — is unchanged since 5.0.0, so existing vaults and
settings files keep working as they are. 5.1.0 includes the security fixes from
5.0.1 and 5.0.2.

## 5.1.0 — WebAssembly thin client

### Changed

- **The web UI is now a Blazor WebAssembly thin client.** The browser runs the
  whole UI and calls the host REST API itself; the host renders no Razor. The
  session token lives in `sessionStorage` (one tab, survives a reload) and is
  attached as a bearer to every request. The Network tab therefore shows the
  `/api` calls — that is by design, and no secret-bearing response is cached
  (`Cache-Control: no-store`, the service worker skips `/api` and `/hubs`).
- **One shared vault.** `DoopassSession` and the per-circuit store are gone.
  The browser and the phone now share the single store in
  `ApiVaultCoordinator`, so concurrent edits can no longer lose each other's
  entries. The TUI keeps its in-process store and converges through the
  cross-process file lock.
- **The WASM bundle is embedded in the host executable** and extracted to a
  version-addressed temp directory on first start, so a single-file publish
  needs no loose `wwwroot/`.
- **`SessionJanitor`** sweeps expired tokens every 30 seconds and relocks the
  vault even when no client makes another request.
- **Response compression** covers the WASM bundle (about 29 MB raw versus
  ~8.5 MB brotli), and the CSP allows `'wasm-unsafe-eval'` for the runtime.

### Fixed

- The browser session maps 409 to busy / uninitialized / duplicate-pair and 422
  to a corrupted store, matching the phone.
- Open-browser now uses the scheme actually resolved from `settings.json`
  (TLS configured there no longer opens the wrong `http://` URL).
- Removed the unreachable Blazor Server host page (`WebUi/Components/App.razor`,
  `Routes.razor`) and its missing `doopass-api.js` reference.

### Tests

247 tests green: Core 92, Tui 50, Api 83, WebClient 22.

## 5.0.2 — security fixes

Follow-up to the 5.0.1 review, fixed before public disclosure.

### Fixed

- **Failed logins no longer lock the owner out.** The master-password throttle
  shared by the REST API and the Blazor login page now slows attempts instead of
  refusing them: after five failures in the last minute each further attempt is
  delayed by 1 s, 2 s, 4 s and then at most 5 s, and a successful login resets
  the counter. A correct password always gets in, whether the failures came from
  another device or from a LAN attacker. The REST `vault-auth` per-IP limiter is
  unchanged and still caps raw request volume.
- **The ErrorBoundary Reload button works under the CSP.** It used an inline
  `onclick`, which `script-src 'self'` blocks; it now uses a Blazor event.
  Inline `onclick` handlers are gone from every Razor source and a test keeps
  them out.
- **Backup restores take the store lock.** Replacing `store.enc` from a backup
  now uses the same cross-process lock as pair mutations, so a write from another
  process can no longer be overwritten (and lost) after the client received 204.
- **The mobile client tells 409s apart.** A busy vault and an uninitialized
  store are no longer reported as "this pair already exists"; the response body
  selects the matching error.
- **The mobile client reports the shared Core version** instead of a hardcoded
  one, so the version shown in the app follows the release.
- **HSTS is not pinned for loopback hosts.** After a `--tls` run on localhost the
  header is no longer stored for `localhost`, `127.0.0.1` or `::1`, so a later
  plain `http://localhost` run is not upgraded into a certificate error.
- **Stale atomic-write temp files are cleaned up** when the same target is
  rewritten, best effort, so a crashed write does not leave debris forever.

### Tests

222 tests green: Core 92, Tui 50, Api 80.

## 5.0.1 — security fixes

Findings from an internal security review, fixed before public disclosure.

### Fixed

- **Backups are confined to the backup directory.** Names supplied through the
  API or the web UI are validated and the resolved path is checked against the
  backup root, so a crafted name can no longer delete or read a file elsewhere
  on the machine. A restore also validates the candidate backup before anything
  is written, so a damaged or foreign file can no longer replace the live store.
- **Settings edits are explicit.** Only `version` and `backup_extension` are
  editable through the UI and the API; everything that names a path is
  read-only there (edit `settings.json` with Doopass stopped instead). The
  backup extension is format-checked.
- **One consistent view of the store.** Mutations take a cross-process lock,
  re-read `store.enc` and write it atomically (temp file + rename). A pair added
  in the browser is no longer lost when the phone adds one at the same time, and
  a crash can no longer leave a half-written store behind.
- **Corrupted stores are reported as such.** A truncated or tampered file gives
  a clear "store is corrupted" answer (422 in the API, a message in the TUI)
  instead of crashing, and an absurd key-derivation cost declared by the file is
  rejected before any CPU is spent on it.
- **Backup numbering.** Twelve backups in one day now produce twelve files;
  the numbering no longer repeats a number and overwrites an existing backup.
- **Login throttling covers the web UI.** Master-password attempts share one
  counter between the REST API and the Blazor login page: five failures per
  minute block further attempts and ten consecutive failures add a 30 s lockout.
- **Session lifetime.** An absolute 8 h cap (`api.absolute_session_ttl_hours`)
  sits on top of the sliding TTL, so an active token cannot live forever. Token
  expiry is reported separately from a real vault relock, so an idle phone no
  longer throws every browser page back to the login screen.
- **Web hardening.** Security headers on every response (`nosniff`, `DENY`
  framing, `no-referrer`, CSP, HSTS over TLS), API and hub traffic excluded from
  the service-worker cache, a loud startup warning when `--lan` runs without
  `--tls`, and 500 responses no longer echo internal exception text.

### Tests

196 tests green: Core 89, Tui 50, Api 57.

## 5.0.0

Thin-client architecture: Blazor Server web UI, REST API + SignalR hub on the
same port, Android thin client, PWA install support, LAN discovery and TLS with
certificate pinning. Store format byte-compatible with the original Python
implementation.
