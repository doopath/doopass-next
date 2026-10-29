# Architecture

## Process model

There is exactly **one executable**: `doopass` (`src/Doopass.Cli`, assembly name
`doopass`). Its first argument selects the mode.

```
doopass                     -> Terminal UI (Doopass.Tui)
doopass serve [options]     -> web host: Blazor UI + REST API + SignalR + discovery
```

Both modes run in a single OS process and use the same `Doopass.Core` storage
layer. There is no background service, no database and no message broker.

```
                        +---------------------------------------------+
                        |                doopass process              |
                        |                                             |
  TERMINAL  <---------->|  Doopass.Tui  ---+                          |
                        |                   |                          |
  BROWSER   <---------->|  Doopass.WebUi ---+--> Doopass.Core         |
                        |   (Blazor        |      Store / Settings   |
                        |    Server)       |      StoreBackup        |
                        |                   |      AppLogger          |
  ANDROID   <--HTTP---->|  REST /api  ------+      Fernet / PBKDF2    |
            <--SignalR-->|  /hubs/vault      |                         |
            <--UDP------>|  Discovery        |                         |
                        +---------------------------------------------+
                                        |
                                  ~/doopass/appdata
                                  store.enc · settings.json
                                  backups/ · doopass.log
```

Key consequences:

* **The host owns the file.** The TUI and the web host both mutate `store.enc`
  directly. The Android client never touches it.
* **Mode is exclusive per process.** A running TUI cannot also serve HTTP; start
  `doopass serve` for the web/API mode.
* **Several host processes would fight over the file.** Each process keeps an
  in-memory copy and rewrites the whole file on every mutation, so running two
  hosts against the same `DOOPASS_HOME` is unsupported (last write wins).

## Projects and dependency rules

| Project | Kind | References | Responsibility |
| ------- | ---- | ---------- | -------------- |
| `Doopass.Core` | class library | BCL only | Crypto, store, backups, settings, paths, logging, password validation |
| `Doopass.WebUi` | Razor Class Library | `Core`, `Microsoft.AspNetCore.Components.Web` | Shared components, CSS/JS assets, `IVaultSession` / `IVaultEvents` contracts |
| `Doopass.Tui` | class library | `Core`, `Terminal.Gui` 2.4.17 | Screens, key routing, monochrome theme |
| `Doopass.Cli` | web app (`Microsoft.NET.Sdk.Web`) | `Core`, `Tui`, `WebUi` | Entry point, web host, REST API, hub, discovery, PWA assets |
| `Doopass.Mobile` | MAUI app (`net10.0-android`) | `Core`, `WebUi`, `SignalR.Client` | Thin client: connect flow, remote session, push client |
| `Doopass.Core.Tests` | xUnit | `Core` | Format compatibility, CRUD, settings, backups, logging |
| `Doopass.Tui.Tests` | xUnit | `Tui` | Key map, focus ring, navigation, screen dumps |
| `Doopass.Api.Tests` | xUnit | `Cli` | Real host on a random port: auth, CRUD, TTL, rate limit |

Dependency rule: **`Core` knows nothing about UI, HTTP or MAUI.** `WebUi` knows
nothing about ASP.NET Core hosting, the REST API or Android. The web host and
the phone are two implementations of the same UI contracts.

`Doopass.WebUi` compiles Razor components against the
`Microsoft.AspNetCore.Components.Web` **NuGet package** (not the shared
framework) so the identical components run inside the MAUI WebView, which has no
ASP.NET Core runtime pack.

## The UI contracts

All screens in `Doopass.WebUi` are written against two interfaces, which is what
makes web UI and Android client share code:

### `IVaultSession`

Awaitable vault operations: state, login/init/logout/reload, key/value CRUD,
backup list/create/restore/delete, settings read/write/restore. Two
implementations:

| Implementation | Host | Behaviour |
| -------------- | ---- | --------- |
| `DoopassSession` (`Cli/Services`) | web host, one per Blazor circuit | wraps synchronous `Store` calls in `ValueTask` |
| `RemoteVaultSession` (`Mobile/Services`) | Android | HTTP calls to the host REST API; maps status codes back to the Core exceptions |

Backup names are **always extensionless** in this contract; each implementation
adds the configured extension itself.

### `IVaultEvents`

Per-scope event relay with four events: `VaultChanged`, `BackupsChanged`,
`SettingsChanged`, `VaultLocked`. `VaultEventRelay` is the concrete relay.

* In the web host, `DoopassSession` subscribes to the server-wide
  `IVaultChangeNotifier` and forwards changes into its circuit's relay, so a
  change made by the API or another browser tab refreshes open pages.
* In the Android client, `VaultPushClient` subscribes to the SignalR hub and
  raises the same events.

Optional host descriptor: `IHostConnection` is registered **only** by the phone.
Pages resolve it with `IServiceProvider.GetService<IHostConnection>()` so the
login screen shows a "Change host" row and an offline banner only where a remote
host actually exists.

## Host pipeline (`doopass serve`)

`WebHostFactory.BuildWebHost` builds the ASP.NET Core application. Order of
registration and middleware matters:

1. Load `Settings`, initialise `AppLogger`, resolve TLS certificate (CLI flag
   wins over `settings.json`), compute its SHA-256 fingerprint.
2. Configure Kestrel: plain HTTP on `localhost:port` (or `0.0.0.0` with
   `--lan`), or HTTPS on all interfaces when a `.pfx` is supplied.
3. Services:
   * singleton `Settings`, `IVaultChangeNotifier`, `ApiVaultCoordinator`,
     `DiscoveryAnnounce`;
   * scoped `VaultEventRelay`, `DoopassSession`, `IVaultSession`, `IVaultEvents`,
     `ToastService`, `ISecretClipboard`;
   * SignalR, rate limiter, hosted `DiscoveryService`, hosted `VaultHubRelay`.
4. Middleware: exception handler → status-code pages (`/not-found`) →
   antiforgery → rate limiter.
5. Endpoints: `/api` group, `/hubs/vault`, embedded asset routes
   (`/app.css`, `/theme.js`, `/app.js`, `/brand.svg`, `/blazor.web.js`,
   `/pwa/*`, favicons), then Razor components with interactive server rendering.

Assets are **embedded resources**, not loose files, so a single-file publish
serves them without a `wwwroot` directory. Static hashed URLs do not exist, so
text assets are served with `Cache-Control: no-cache` to prevent stale CSS/JS
after an upgrade.

## Session and locking model

Two independent session types exist in the host:

```
Browser circuit                          REST/API clients
---------------                          ----------------
DoopassSession (scoped)                  ApiVaultCoordinator (singleton)
   owns a Store instance                    owns one shared Store
   unlocked per circuit                     unlocked for the whole host
   logout -> drops its store                token dictionary with sliding TTL
                                            last token gone -> relock + notice
```

* The **web UI** is unlocked per browser circuit. Opening a second browser
  session requires the master password again, even while the first is open.
* The **REST API** keeps a single unlocked store for all tokens; tokens are
  random 32-byte base64url strings, valid while used within the TTL (default 30
  minutes, configurable through `api.session_ttl_minutes`). When the last token
  is removed (logout or expiry) the store is dropped from memory and clients are
  told `vaultLocked`.
* Both paths publish mutation events through `IVaultChangeNotifier`, which fans
  out to (a) the SignalR hub for API clients and (b) every circuit's relay.

Because the two session types hold **separate** `Store` instances, consistency
between web pages and API clients is achieved by re-reading the file on change
events, not by sharing memory. That is the main architectural sharp edge; see
[Known Limitations](Known-Limitations.md#two-in-memory-store-instances).

## Persistence model

* `store.enc` is rewritten in full on every mutation (no append, no journal).
* Writes are not atomic at the filesystem level (write text, no temp-file rename
  and no fsync).
* `settings.json` is rewritten in full on every change.
* Backups are byte copies of `store.enc`, so they remain encrypted.
* There are **no file locks**; correctness relies on a single writer process.

See [Business Rules](Business-Rules.md) for the exact semantics and
[Data Formats](Data-Formats.md) for the bytes.

## Android thin client

```
MAUI app (one root scope)
  Vault (ContentPage)
    BlazorWebView -> AppRoot -> ConnectGuard -> Routes (shared RCL)
  ConnectionStore      SecureStorage: host, pinned fingerprint, token
  HostDiscovery        UDP 7241 broadcast, 1.5 s window
  RemoteVaultSession   HTTP  -> host /api
  VaultPushClient      SignalR -> host /hubs/vault
  HostConnectionState  IHostConnection implementation
  AndroidClipboard     native ClipboardManager
```

The phone stores **no vault**. Its local state is three strings (host URL,
certificate pin, session token) plus a log file in the app sandbox.

`ConnectGuard` is the router: on first start it loads the saved host, probes
`/api/vault/state`, then routes to `/connect`, `/` (login) or `/store`. It also
starts the push channel when a token exists and the host answered.

## TUI architecture

`DoopassWindow` is the root `Window`: header bar, screen stack, footer bar and
the single global key route. Screens are `View` subclasses that never handle
Tab, Esc, Q or F1 themselves.

```
DoopassWindow
  stack: MainMenu -> Login/InitStore -> Store -> PairForm ...
  RouteKey (IKeyboard.KeyDown, before any focused view)
    -> lock (G), list navigation, detail scrolling,
       screen shortcuts (store v/e/d/a, backups c/r/d, settings 0),
       then TuiKeyMap.Resolve for FocusNext/FocusPrevious/Back/Quit/Help
```

`TuiKeyMap` is a pure function of `(Key, KeyRouteContext)` so key behaviour is
unit-tested without a terminal. See [Application: TUI](Application-TUI.md).

## Where state lives

| State | Location | Lifetime |
| -------- | -------- | -------- |
| Secrets | `~/doopass/appdata/store.enc` | persistent, encrypted |
| Master password | process memory (a `Store` instance) | until lock/exit |
| API tokens | `ApiVaultCoordinator` dictionary | TTL-bounded, memory only |
| Web session | Blazor circuit | circuit lifetime |
| Settings | `settings.json` | persistent |
| Theme choice | browser `localStorage` key `doopass-theme` | per browser |
| Phone host/pin/token | Android `SecureStorage` | persistent, Keystore-backed |
| Logs | `appdata/doopass.log` (+ `.log.1`) | rotated at 5 MB |
