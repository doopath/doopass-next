# E2E verification — thin-client architecture

Status after the four-phase implementation (the API contract is documented in
[the REST API section of the README](../README.md#rest-api-and-thin-clients)).
Environment: Windows host (MONSTERMACHINE), .NET SDK 10.0.402, `maui-android`
workload 36.1.2.

## Automated coverage (`dotnet test`, 222 tests green)

| Area                              | Result                                                                    |
| --------------------------------- | ------------------------------------------------------------------------- |
| Store format compatibility        | 92 / 92 Core.Tests (incl. concurrency, corruption, settings policy)        |
| TUI behaviour                     | 50 / 50 Tui.Tests                                                          |
| API integration                   | 80 / 80 Api.Tests incl. `T01`–`T05` auth lifecycle                        |
| Pair CRUD (unicode, 404, 409)     | covered in `VaultDataTests`                                                |
| Backup lifecycle (safety backup)  | covered in `VaultDataTests.T02`                                            |
| Settings round trip               | `VaultSettingsTests` (path keys 403, bad extension 400)                    |
| Backup name traversal             | `BackupNameTraversalTests` (404 everywhere, store hash unchanged)          |
| Backup restore validation         | `BackupRestoreValidationTests` (422/401, store untouched)                  |
| Backup restore locking            | `BackupTests.Restore_takes_the_store_lock` (busy → 409, store unchanged)   |
| Corrupted store                   | `CorruptStoreTests`, `CorruptedStoreApiTests` (422, fast rejection)        |
| Login throttle (API + Blazor)     | `AuthThrottleTests`, `DoopassSessionThrottleTests` (delay only, no lockout)|
| Inline handlers vs CSP            | `InlineEventHandlerTests` (no `onclick=` in WebUi/Mobile Razor sources)    |
| Mobile 409 mapping                | `VaultResponseErrorsTests` (busy / uninitialized / duplicate pair)         |
| Stale temp-file sweep             | `AtomicFileTests`                                                          |
| HSTS on loopback                  | `SecurityHeadersTests.ShouldSendHsts` cases                                |
| Session/vault event semantics     | `VaultEventSemanticsTests`                                                 |
| Absolute session cap              | `AbsoluteSessionTtlTests`                                                  |
| Security headers                  | `SecurityHeadersTests`                                                     |
| Error mapping (no internals)      | `ErrorMappingTests`                                                        |
| Token logout → 401 everywhere     | covered in `VaultTokenTests`                                               |
| Sliding TTL expiry + relock       | covered in `VaultTtlTests` (fake clock on the coordinator)                  |
| Rate limit (5/min → 429)          | covered in `RateLimitTests`                                                |
| Discovery reply format            | covered in `DiscoveryPayloadTests`                                         |
| Web host pipeline                 | real host built on `127.0.0.1:0` inside every fixture                       |

## Manual smoke tests (curl + PowerShell helpers)

| Scenario                                       | Result        | Notes                                                      |
| ---------------------------------------------- | ------------- | ---------------------------------------------------------- |
| Anonymous `GET /api/vault/state`               | OK            | `{storeExists, unlocked, serverVersion, certFingerprint}`   |
| `POST /api/auth/init` → token issued           | OK            | base64url, 43 chars                                        |
| Wrong password login                           | 401 `{"error":"Incorrect password!"}`                      |
| Pair add / duplicate / value / update / delete | 204 / 409 / 200 / 204 / 204–404                            |
| Backup create / restore / delete               | 204 each; safety backup visible in the log                 |
| Settings GET / PUT / restore-defaults          | 200 / 403 (path keys) / 400 (bad extension) / 204          |
| Hub negotiate with token                       | 200, connectionId issued                                   |
| Logout then protected calls                    | 204 → 401 `{error: "Session expired…"}`                     |
| Discovery probe (`--lan`)                      | `{"service":"doopass","name":…,"port":…,"tls":false,"fingerprint":null,"version":"5.0.2"}` |
| TLS mode (`--tls pfx`)                         | HTTPS 200; discovery reply `tls:true` + fingerprint; plain HTTP rejected | fingerprint also logged at startup |
| Single-file publish (`scripts/publish-portable`) | OK       | exe serves `app.css` (29 771 bytes) from the RCL resources  |
| Release APK                                    | `io.github.doopath.doopass-Signed.apk` (62.5 MB) published with AOT/trim disabled for the first release |

## Matrix that needs physical devices (deferred, documented steps)

The following remain as a manual checklist for a machine with an Android
device/emulator + Wi-Fi LAN; the code paths for each are implemented and the
host side of each step is already covered above.

- [ ] Discovery on the phone finds the running host (same Wi-Fi, inbound UDP
      7241 and the API TCP port allowed in the PC firewall).
- [ ] Manual entry works; on the emulator `http://10.0.2.2:7240`; over USB,
      `adb reverse tcp:7240 tcp:7240` + `127.0.0.1:7240`.
- [ ] Login with the master password; full pair CRUD; copy to clipboard
      (native MAUI clipboard + fallback reveal).
- [ ] Change host is visible on the login screen even while the current host
      answers → picker opens, Back to sign in returns to the login card with
      the same saved host.
- [ ] Stop the host (or move it to another IP) while the phone shows the login
      screen → the offline banner appears in about four seconds, the saved
      token is not cleared, and Change host finds the new address through
      discovery or manual entry.
- [ ] Phone-side pair change appears in the browser without reload and vice
      versa (via `vaultChanged` through `/hubs/vault` + the per-circuit relay).
- [ ] Backup restore on the phone: browser updates; safety backup created.
- [ ] TTL expiry / `vaultLocked` event drops the phone to the login screen.
- [ ] Logout on the phone → all host requests answer 401; six rapid login
      attempts answer 429.
- [ ] TLS: the phone shows the pinned fingerprint; substituting a different
      self-signed certificate is rejected by `ServerCertificateCustomValidationCallback`.
- [ ] Screen rotation keeps the vault state (WebView keeps its component tree).
