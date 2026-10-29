# Application: Android Thin Client (`Doopass.Mobile`)

A MAUI application (`net10.0-android`, API 23+) that hosts the shared Blazor UI
in a `BlazorWebView` and talks to a Doopass host over the REST API. **It stores
no vault.**

```
src/Doopass.Mobile
├── MauiProgram.cs             DI, logging, unhandled-exception hooks
├── App.cs, Vault.cs           MAUI window + the single BlazorWebView page
├── Components/
│   ├── AppRoot.razor          ConnectGuard + shared Routes
│   ├── ConnectGuard.razor     route guard / session bootstrap
│   ├── Connect.razor          host discovery, manual entry, pin confirmation
│   └── ConnectLayout.razor    layout for the connect flow
├── Services/
│   ├── ConnectionStore.cs     SecureStorage: host, pin, token
│   ├── HostDiscovery.cs       UDP broadcast scan
│   ├── RemoteVaultSession.cs  IVaultSession over HTTP
│   ├── VaultPushClient.cs     SignalR change channel
│   ├── HostConnectionState.cs IHostConnection implementation
│   └── AndroidClipboard.cs    native ClipboardManager
└── Platforms/Android/         MainActivity, MainApplication
```

## Lifecycle

1. `MauiProgram.CreateMauiApp` registers services, initialises `AppLogger` at
   `FileSystem.AppDataDirectory/doopass.log`, and attaches handlers for
   `AppDomain.UnhandledException` and Android's unhandled-exception raiser.
2. `App.CreateWindow` resolves `Vault` from DI — a `ContentPage` containing a
   `BlazorWebView` rooted at `wwwroot/index.html`, mounting `AppRoot` into
   `#app`.
3. `ConnectGuard.OnAfterRenderAsync(firstRender)` loads `ConnectionStore` and
   routes the user.

DI lifetimes: singletons for the phone's stateful services
(`ConnectionStore`, `HostConnectionState`/`IHostConnection`, `HostDiscovery`,
`VaultPushClient`), scoped for the UI-facing ones (`VaultEventRelay`,
`RemoteVaultSession`/`IVaultSession`, `IVaultEvents`, `ToastService`,
`ISecretClipboard` → `AndroidClipboard`). The MAUI root scope lives for the
whole app, so "scoped" effectively means "one session per app launch".

## Connect flow (`/connect`)

Two tabs:

**Automatic** — runs `HostDiscovery.DiscoverAsync()` immediately on open (and on
"Search again"):

* sends the ASCII probe `DOOPASS_DISCOVER` to `255.255.255.255:7241` **and** to
  every directed broadcast of active Ethernet/Wi-Fi interfaces (e.g.
  `192.168.1.255`), de-duplicated;
* opens the receive loop once, with a 1.5 s window (a second outstanding
  `ReceiveAsync` is rejected by `UdpClient`, which is why the old polling loop
  failed silently);
* requires `SO_BROADCAST` (`UdpClient.EnableBroadcast`), binds to an ephemeral
  port, and acquires an Android multicast lock so broadcast replies are not
  filtered;
* accepts replies whose `service` is `doopass` and port is in `1..65535`,
  deriving the host URL from the UDP sender address and the scheme from the
  `tls` flag.

**Manual** — free-text address. `Sanitize` prefixes `http://` when no scheme is
given, trims whitespace and trailing slashes. Pressing Enter probes the host.

### Confirmation step

Selecting a host (discovered or manual) runs a probe of
`GET /api/vault/state` (6 s timeout) and shows a "Trust this host?" card with:

* host name and URL;
* an **Encrypted · TLS** badge plus the SHA-256 fingerprint when the reply says
  `tls: true`;
* **Trust and connect** (saves host + pin, clears the old token) and **Back**.

The probe deliberately accepts any certificate — there is no pin yet — and takes
the fingerprint from the certificate seen in the handshake, falling back to the
JSON field `certFingerprint`. The handshake value wins so a spoofed body cannot
choose the pin. With plain HTTP the app shows a permanent unencrypted-connection
warning.

`TrustAsync` calls `Connections.SaveHostAsync(url, tls ? fingerprint : null)`,
then `RemoteVaultSession.GetStateAsync()` to confirm the host answers and to
read its version, then navigates to the login screen.

## Route guard (`ConnectGuard`)

Runs on first render and on every navigation/lock event.

| Saved host | Probe result | Destination |
| ---------- | ------------ | ----------- |
| none | — | `/connect` |
| present | state unlocked **and** a token exists | `/store` |
| present | offline (timeout/refused) | `/` (login, with offline banner) — token kept |
| present | reachable, locked, no usable token | token cleared → `/` |
| present | on `/connect` already | stays, push channel started |

It also starts the SignalR push connection when host + token exist and the host
is reachable (`StartPushIfNeeded`), skipping hosts the last probe marked
unreachable. A `VaultLocked` event routes to `/`.

## `RemoteVaultSession`

An `IVaultSession` over HTTP.

| Concern | Behaviour |
| ------- | --------- |
| Base URL | `ConnectionStore.HostUrl`; client rebuilt when host, pin or token change |
| Auth | `Authorization: Bearer <token>` on every request |
| Timeouts | state probe 4 s, auth 8 s, general `HttpClient.Timeout` 20 s |
| State | `/api/vault/state`; `unlocked` counts only when this client holds a token |
| Offline | timeout/connection failure → `VaultState.IsOffline = true`, host marked unreachable |
| Login/Init | POST credentials, store the returned token, force a client rebuild |
| Logout | POST (4 s cap), always clears the local token, network failure tolerated |
| Reload | no-op — the remote session always reflects the host |
| Pinning | when `https` + pin: SHA-256 of the server certificate's DER bytes must match (hex, case-insensitive) |
| Status mapping | 401 → clear token + raise lock + `UnauthorizedAccessException`; 404 → `PropertyDoesNotExistError`; 409 → `PropertyAlreadyExistError`; 429 → `InvalidOperationException`; other → `HttpRequestException` |
| Logging | failures logged without bodies or secrets |

## `VaultPushClient`

SignalR connection to `<host>/hubs/vault` with the token as
`AccessTokenProvider`.

* automatic reconnect: 0 s, 1 s, 5 s, 15 s, 30 s;
* forwards `vaultChanged`, `backupsChanged`, `settingsChanged` into the shared
  `VaultEventRelay`;
* on `vaultLocked` **for the current host and token**, clears the stored token
  and raises `VaultLocked`; late events from a previous host/token are ignored;
* `IsCurrentSession` guards against reusing a connection that belongs to an old
  host or token.

## Local storage

`ConnectionStore` keeps three strings in Android `SecureStorage`:

| Key | Content |
| --- | ------- |
| `doopass.host` | base URL, trailing slash trimmed |
| `doopass.pin` | SHA-256 hex fingerprint, lowercase (only for HTTPS) |
| `doopass.token` | API session token |

`SaveHostAsync` always clears the old token (a new host means a new login).
`ClearAsync` forgets all three. Storage failures are logged and swallowed so a
Keystore problem cannot brick the app.

## Clipboard

`AndroidClipboard` writes through `ClipboardManager`. It exists because the
WebView clipboard API requires a live user gesture, and the UI awaits a vault
read before copying — the gesture is gone by then, which used to surface as
"Clipboard is blocked". The native path has no such requirement.

## Build configuration

| Setting | Value | Why |
| ------- | ----- | --- |
| `ApplicationId` | `io.github.doopath.doopass` | Play/package identity |
| `SupportedOSPlatformVersion` | 23 | BlazorWebView minimum |
| `AndroidPackageFormat` | `apk` | installable output |
| `RunAOTCompilation`, `PublishTrimmed`, `AndroidEnableProfiledAot` | `false` | full IL for readable diagnostics; enabling requires `TrimmerRootDescriptor.xml` for System.Text.Json/SignalR reflection |
| `EmbedAssembliesIntoApk` (Debug) | `true` | a plain `adb install` of a Fast-Deployment APK aborts with "No assemblies found in '.override'" |
| `MauiIcon` | `appicon.png`, `Color=#0B0616`, `ForegroundScale=0.6667` | adaptive-icon mask reveals the central 2/3 |

Build scripts: `scripts/build-android.sh` / `.ps1` (Debug APK),
`dotnet publish src/Doopass.Mobile -c Release -f net10.0-android` for release
APK/AAB. This project is **not** part of `Doopass.slnx` and is not built by
`dotnet build Doopass.slnx`, because it needs the `maui-android` workload.

## Testing on the phone

* The emulator cannot receive LAN broadcasts — enter `http://10.0.2.2:7240`
  manually.
* `adb reverse tcp:7240 tcp:7240` plus `127.0.0.1:7240` works over USB; use
  `https://…` when the host runs with `--tls`.
* A stale host address is reported after ~4 s instead of waiting for the full
  request timeout, and the login screen offers **Change host**.
* Only one host process should run on the PC: an old `doopass serve` on the same
  port answers with an old build and looks like "host unreachable".
