# Changelog

Notable changes to Doopass. The on-disk format — `store.enc`, `settings.json`
and backup file names — is unchanged since 5.0.0: 5.0.1 only closes security
issues, so existing vaults and settings files keep working as they are.

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
