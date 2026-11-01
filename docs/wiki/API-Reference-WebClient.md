# API Reference — Doopass.WebClient

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `Doopass.WebClient` · Source: `src/Doopass.WebClient/bin/Release/net10.0/Doopass.WebClient.xml`

## Types

* [`Doopass.WebClient.WebApiVaultSession`](#webapivaultsession)
* [`Doopass.WebClient.WebVaultPushClient`](#webvaultpushclient)

## `Doopass.WebClient`

### `WebApiVaultSession`

IVaultSession for the Blazor WebAssembly thin client: every call is a direct
HTTP request against the host REST API. HTTP statuses map back to Core
exceptions so the shared RCL pages behave identically across hosts. Token
lives only in memory plus sessionStorage (scoped to one tab, survives
reload), and the bearer header is attached to each outgoing request.

* `WebApiVaultSession(HttpClient httpClient, IJSRuntime js, VaultEventRelay relay)` — Creates the browser session over the host's same-origin HTTP client.
  * `httpClient` — Client whose base address is the Blazor app origin.
  * `js` — Browser interop used for sessionStorage.
  * `relay` — Per-app event relay shared with the UI.
* `BuildRequest(HttpMethod, string, HttpContent)` — Builds a request for the current bearer token. No token means no Authorization header.
* `InitializeAsync()` — Restores the local session from sessionStorage before the first render, so an unlocked tab survives a reload (or opens in a fresh one) without a new login. The bearer is then attached to each outgoing request.
* `PersistTokenAsync(string)` — Stores the token in memory and sessionStorage, and ends the local session when clearing. Raising `SessionCleared` lets the host stop the SignalR push connection that belongs to the now-absent token.
* `PostAuthJsonAsync(string, object, CancellationToken)` — Sends a login/init body; transport failures surface as `HostUnreachableException` so the login screen shows the host banner.
* `SessionActivated` — Raised after the local session is created (login or init), so hosts can open per-session resources such as the push channel.
* `SessionCleared` — Raised after the local session ended (logout or a server 401/lock), so hosts can stop per-session resources.

### `WebVaultPushClient`

SignalR push channel for the Blazor WebAssembly thin client: keeps open
pages fresh. The token comes from sessionStorage (one tab, survives reload);
a lock event clears it and raises `VaultLocked` so the guard routes to
login. The connection is started explicitly after the session is ready and
stopped when the session ends.

* `WebVaultPushClient(IJSRuntime js, VaultEventRelay relay, string hubBaseAddress)` — Creates the push channel for the host that served the WASM app.
  * `js` — Browser interop used to read the stored token.
  * `relay` — Per-app event relay shared with the UI.
  * `hubBaseAddress` — Origin of the host that serves the app.
* `StartAsync()` — Opens the SignalR hub for the token stored in sessionStorage.
* `StopAsync()` — Stops the channel when the session ends, but keeps the gate alive so a later login in the same tab can start it again.
