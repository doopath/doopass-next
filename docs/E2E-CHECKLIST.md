# E2E verification — thin-client architecture

Status after the four-phase implementation (see `THIN_CLIENT_PROMPT.md` for the
spec). Environment: Windows host (MONSTERMACHINE), .NET SDK 10.0.402,
`maui-android` workload 36.1.2.

## Automated coverage (`dotnet test`, 103 tests green)

| Area                              | Result                                                                    |
| --------------------------------- | ------------------------------------------------------------------------- |
| Store format compatibility        | 34 / 34 Core.Tests (unchanged by this work)                                |
| TUI behaviour                     | 50 / 50 Tui.Tests (unchanged by this work)                                 |
| Auth flow (init/login/401/409)    | 19 / 19 Api.Tests incl. `T01`–`T05` auth lifecycle                         |
| Pair CRUD (unicode, 404, 409)     | covered in `VaultDataTests`                                                |
| Backup lifecycle (safety backup)  | covered in `VaultDataTests.T02`                                            |
| Settings round trip               | covered in `VaultSettingsTests`                                            |
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
| Settings GET / PUT / restore-defaults          | 200 / 204; substituted paths round-trip                    |
| Hub negotiate with token                       | 200, connectionId issued                                   |
| Logout then protected calls                    | 204 → 401 `{error: "Session expired…"}`                     |
| Discovery probe (`--lan`)                      | `{"service":"doopass","name":…,"port":…,"tls":false,"fingerprint":null,"version":"5.0.0"}` |
| TLS mode (`--tls pfx`)                         | HTTPS 200; discovery reply `tls:true` + fingerprint; plain HTTP rejected | fingerprint also logged at startup |
| Single-file publish (`scripts/publish-portable`) | OK       | exe serves `app.css` (29 771 bytes) from the RCL resources  |
| Release APK                                    | `io.github.doopath.doopass-Signed.apk` (62.5 MB) published with AOT/trim disabled for the first release |

## Matrix that needs physical devices (deferred, documented steps)

The following remain as a manual checklist for a machine with an Android
device/emulator + Wi-Fi LAN; the code paths for each are implemented and the
host side of each step is already covered above.

- [ ] Discovery on the phone finds the running host (same Wi-Fi).
- [ ] Manual entry works; on the emulator `http://10.0.2.2:7240`.
- [ ] Login with the master password; full pair CRUD; copy to clipboard
      (WebView clipboard + fallback reveal).
- [ ] Phone-side pair change appears in the browser without reload and vice
      versa (via `vaultChanged` through `/hubs/vault` + the per-circuit relay).
- [ ] Backup restore on the phone: browser updates; safety backup created.
- [ ] TTL expiry / `vaultLocked` event drops the phone to the login screen.
- [ ] Logout on the phone → all host requests answer 401; six rapid login
      attempts answer 429.
- [ ] TLS: the phone shows the pinned fingerprint; substituting a different
      self-signed certificate is rejected by `ServerCertificateCustomValidationCallback`.
- [ ] Screen rotation keeps the vault state (WebView keeps its component tree).
