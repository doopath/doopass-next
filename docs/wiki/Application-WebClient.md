# Application: WebAssembly Client (`Doopass.WebClient`)

The browser-side host of the shared Razor components. It is a
`Microsoft.NET.Sdk.BlazorWebAssembly` project that runs entirely in the
browser, calls the host REST API and `/hubs/vault`, and renders no server-side
Razor. `Doopass.Cli` references it only to embed and serve its published
`wwwroot/`.

```
src/Doopass.WebClient
├── Program.cs              WASM host, DI, token restore, push startup
├── App.razor               router over the shared Doopass.WebUi pages
├── _Imports.razor          shared namespaces
├── WebApiVaultSession.cs   IVaultSession over the REST API
├── WebVaultPushClient.cs   SignalR client + session lifecycle
└── wwwroot/index.html      app shell, PWA links, framework script
```

## Startup

`Program.cs` registers one `HttpClient` whose base address is the app origin,
`VaultEventRelay`, `WebApiVaultSession` (also exposed as `IVaultSession`),
`ToastService`, `JsClipboard` and `WebVaultPushClient`. Before the first render
it:

1. restores the bearer token from `sessionStorage` (`InitializeAsync`);
2. starts the SignalR connection when a token exists;
3. subscribes the session's `SessionActivated` / `SessionCleared` events to
   start and stop that connection.

There is no `HttpClient.BaseAddress` read at runtime: the hub URL is built from
`IWebAssemblyHostEnvironment.BaseAddress` in the constructor (the getter throws
in WebAssembly).

## `WebApiVaultSession`

Every `IVaultSession` call is a same-origin HTTP request. The token lives in
memory and `sessionStorage`; `Bearer <token>` is attached to each request.
Status mapping:

| Status | Exception |
| ------ | --------- |
| 401 | `UnauthorizedAccessException`; clears the token and raises `VaultLocked` |
| 403 | `ReadOnlySettingError` |
| 404 | `PropertyDoesNotExistError` |
| 409 | `VaultResponseErrors.Conflict` → busy / uninitialized / duplicate key |
| 422 | `StoreCorruptedError` |
| 429 | `InvalidOperationException` |
| transport failure | `HostUnreachableException` on auth calls, offline state on `/state` |

`GetStateAsync` reports `unlocked` only when the server says the vault is open
**and** this tab holds a token, so a fresh tab starts on the login screen even
when another client unlocked the vault.

## Token and lock lifecycle

* Login/init stores the token in `sessionStorage` (`doopass_vault_token`).
* Logout calls `POST /api/auth/logout`, clears the token and stops the hub.
* A 401 or a hub `vaultLocked` clears the token and routes to `/`.
* The hub token is validated on connect; a `vaultLocked` for another token does
  not end this tab.

Closing the tab leaves the host store open until the TTL; `SessionJanitor` on
the host performs the actual relock.

## Build and embedding

`Doopass.Cli` runs `dotnet publish` on this project from the `PackWasmBundle`
MSBuild target, zips the published `wwwroot/` into
`Doopass.Cli.wwwroot.wasm.zip` and embeds it in the host assembly. At runtime
`WebHostFactory` extracts that zip to `%TEMP%/doopass-web/<version>-<hash>/` and
serves it with static-file middleware plus an SPA fallback. Trimming and AOT are
disabled here; the host never publishes this project as a single file.

See [Application: Web UI](Application-Web-UI.md) for the shared components and
[API Reference: WebClient](API-Reference-WebClient.md) for the public surface.
