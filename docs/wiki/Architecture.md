# Architecture

## Process model

There is exactly **one executable**: `doopass` (`src/Doopass.Cli`, assembly name
`doopass`). Its first argument selects the mode.

```
doopass                     -> Terminal UI (Doopass.Tui)
doopass serve [options]     -> web host: static WASM client + REST API + SignalR + discovery
```

Both modes run in a single OS process and use the same `Doopass.Core` storage
layer. There is no background service, no database and no message broker.

```
                        +---------------------------------------------+
                        |                doopass process              |
                        |                                             |
  TERMINAL  <---------->|  Doopass.Tui  ---+                          |
                        |                   |                          |
  BROWSER   <--HTTP---->|  static WASM  ----+--> Doopass.Core         |
            <--SignalR-->|  REST /api        |      Store / Settings   |
                        |  /hubs/vault      |      StoreBackup        |
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

* **The host owns the file.** The TUI mutates `store.enc` directly; the browser
  and the phone go through `ApiVaultCoordinator`, which holds the single shared
  `Store`. The thin clients never touch the file.
* **Mode is exclusive per process.** A running TUI cannot also serve HTTP; start
  `doopass serve` for the web/API mode.
* **Several host processes would fight over the file.** Each process keeps an
  in-memory copy and rewrites the whole file on every mutation. Running two
  hosts against the same `DOOPASS_HOME` is still unsupported: the file lock
  prevents clobbering, but each host has its own event fan-out.

## Projects and dependency rules

| Project | Kind | References | Responsibility |
| ------- | ---- | ---------- | -------------- |
| `Doopass.Core` | class library | BCL only | Crypto, store, backups, settings, paths, logging, password validation |
| `Doopass.WebUi` | Razor Class Library | `Core`, `Microsoft.AspNetCore.Components.Web` | Shared components, CSS/JS assets, `IVaultSession` / `IVaultEvents` contracts |
| `Doopass.WebClient` | Blazor WebAssembly app | `Core`, `WebUi`, `SignalR.Client` | Browser thin client: REST session, push channel, host page |
| `Doopass.Tui` | class library | `Core`, `Terminal.Gui` 2.4.17 | Screens, key routing, monochrome theme |
| `Doopass.Cli` | web app (`Microsoft.NET.Sdk.Web`) | `Core`, `Tui`, `WebUi`, `WebClient` | Entry point, web host, REST API, hub, discovery, embedded assets |
| `Doopass.Mobile` | MAUI app (`net10.0-android`) | `Core`, `WebUi`, `SignalR.Client` | Thin client: connect flow, remote session, push client |
| `Doopass.Core.Tests` | xUnit | `Core` | Format compatibility, CRUD, settings, backups, logging |
| `Doopass.Tui.Tests` | xUnit | `Tui` | Key map, focus ring, navigation, screen dumps |
| `Doopass.Api.Tests` | xUnit | `Cli`, `WebClient` | Real host on a random port: auth, CRUD, TTL, security, browser contract |
| `Doopass.WebClient.Tests` | xUnit | `WebClient` | SessionStorage flow, status mapping, push lifecycle |

Dependency rule: **`Core` knows nothing about UI, HTTP or MAUI.** `WebUi` knows
nothing about ASP.NET Core hosting, the REST API or Android. The browser and
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
backup list/create/restore/delete, settings read/write/restore. Two thin-client
implementations, both speaking HTTP to the host REST API and mapping statuses
back to the Core exceptions:

| Implementation | Host | Behaviour |
| -------------- | ---- | --------- |
| `WebApiVaultSession` (`WebClient`) | browser (Blazor WebAssembly) | same-origin `HttpClient`; bearer from `sessionStorage`; maps 401/403/404/409/422/429 |
| `RemoteVaultSession` (`Mobile/Services`) | Android | pinned TLS, four-second host probe, token in `SecureStorage` |

The TUI does not use this contract; it calls `Store` directly in-process.

Backup names are **always extensionless** in this contract; each implementation
adds the configured extension itself.

### `IVaultEvents`

Per-scope event relay with four events: `VaultChanged`, `BackupsChanged`,
`SettingsChanged`, `VaultLocked`. `VaultEventRelay` is the concrete relay.

* In the browser, `WebVaultPushClient` subscribes to `/hubs/vault` and raises
  the same events, so a change made by the phone or the TUI refreshes open pages.
* In the Android client, `VaultPushClient` does the same over the same hub.
* The host's `VaultHubRelay` is the only publisher: it subscribes to the
  server-wide `IVaultChangeNotifier`.

Optional host descriptor: `IHostConnection` is registered **only** by the phone.
Pages resolve it with `IServiceProvider.GetService<IHostConnection>()` so the
login screen shows a "Change host" row and an offline banner only where a remote
host actually exists.

## Host pipeline (`doopass serve`)

`WebHostFactory.BuildWebHost` builds the ASP.NET Core application. Order of
registration and middleware matters:

1. Load `Settings`, initialise `AppLogger`, resolve TLS certificate (CLI flag
   wins over `settings.json`), compute its SHA-256 fingerprint. `--lan` without
   TLS prints a loud plaintext warning.
2. Configure Kestrel: plain HTTP on `localhost:port` (or `0.0.0.0` with
   `--lan`), or HTTPS on all interfaces when a `.pfx` is supplied.
3. Services:
   * singleton `Settings`, `IVaultChangeNotifier`, `AuthThrottle`,
     `ApiVaultCoordinator`, `DiscoveryAnnounce`;
   * SignalR, rate limiter, response compression, hosted `DiscoveryService`,
     hosted `VaultHubRelay`, hosted `SessionJanitor`.
4. Middleware: security headers (CSP, nosniff, DENY framing, no-referrer) →
   response compression → exception handler → rate limiter.
5. Endpoints: `/api` group, `/hubs/vault`, embedded PWA routes (`/pwa/*`,
   favicons), then the extracted WASM bundle as static files and an SPA fallback
   for client routes.

Assets are **embedded resources**, not loose files: the WebAssembly bundle is
zipped into the host assembly and extracted to a version-addressed temp folder
on first start. Static hashed URLs do not exist, so text assets are served with
`Cache-Control: no-cache`; the framework files are compressed on the fly.

## Session and locking model

The host has **one** session type for every client:

```
Browser tab                 Android phone                 TUI
-----------                 -------------                 ---
WebApiVaultSession          RemoteVaultSession            direct Store
  token in sessionStorage     token in SecureStorage       in-process
  bearer on every call        bearer on every call
        \                        /
         \                      /
          ApiVaultCoordinator (singleton)
             one shared Store
             token dictionary (sliding + absolute TTL)
             SessionJanitor sweeps expired tokens
             last token gone -> relock + vaultLocked
```

* Every browser call is authenticated with a bearer token; there is no cookie
  and no server-side circuit. The token is random 32-byte base64url, valid while
  used within the sliding TTL (default 30 min, `api.session_ttl_minutes`) and
  never past the absolute cap (default 8 h, `api.absolute_session_ttl_hours`).
* `SessionJanitor` runs every 30 seconds and prunes expired tokens even when no
  client makes a request, so the decrypted store does not sit in host memory
  after the last tab or phone goes away.
* All mutation events go through `IVaultChangeNotifier`; `VaultHubRelay` pushes
  them to `/hubs/vault`. A single expired token is told on its own group;
  `vaultLocked` is broadcast only when no session remains.
* Mutations take a cross-process `StoreFileLock`, re-read `store.enc` and write
  it atomically (temp file + rename), so the browser, phone and TUI cannot lose
  each other's entries.

## Persistence model

* `store.enc` is rewritten in full on every mutation (no append, no journal).
* Writes go through `AtomicFile`: temp file in the same directory, then rename,
  so a crash or full disk can never leave a half-written vault.
* Mutations also take a cross-process `StoreFileLock` and re-read the file
  before writing, so two clients converge instead of overwriting each other.
* `settings.json` is rewritten in full on every change (also atomically).
* Backups are byte copies of `store.enc`, so they remain encrypted.

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
| Master password | process memory (a `Store` instance) | until relock/TTL/exit |
| API tokens | `ApiVaultCoordinator` dictionary | sliding + absolute TTL, memory only |
| Browser session token | `sessionStorage` | one tab, survives reload |
| Phone session token | Android `SecureStorage` | Keystore-backed |
| Settings | `settings.json` | persistent |
| Theme choice | browser `localStorage` key `doopass-theme` | per browser |
| Phone host/pin/token | Android `SecureStorage` | persistent, Keystore-backed |
| Logs | `appdata/doopass.log` (+ `.log.1`) | rotated at 5 MB |
