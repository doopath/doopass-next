# API Reference — doopass

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `doopass` · Source: `src/Doopass.Cli/bin/Release/net10.0/doopass.xml`

## Types

* [`Doopass.Cli.Api.ApiSession`](#apisession)
* [`Doopass.Cli.Api.ApiVaultCoordinator`](#apivaultcoordinator)
* [`Doopass.Cli.Api.DiscoveryAnnounce`](#discoveryannounce)
* [`Doopass.Cli.Api.DiscoveryService`](#discoveryservice)
* [`Doopass.Cli.Api.IVaultChangeNotifier`](#ivaultchangenotifier)
* [`Doopass.Cli.Api.NoStoreFilter`](#nostorefilter)
* [`Doopass.Cli.Api.RequireVaultTokenFilter`](#requirevaulttokenfilter)
* [`Doopass.Cli.Api.SessionJanitor`](#sessionjanitor)
* [`Doopass.Cli.Api.VaultAuth`](#vaultauth)
* [`Doopass.Cli.Api.VaultChangeNotifier`](#vaultchangenotifier)
* [`Doopass.Cli.Api.VaultEndpoints`](#vaultendpoints)
* [`Doopass.Cli.Api.VaultHub`](#vaulthub)
* [`Doopass.Cli.Api.VaultHubRelay`](#vaulthubrelay)
* [`Doopass.Cli.Api.WebHostFactory`](#webhostfactory)
* [`Doopass.Cli.Api.WebHostOptions`](#webhostoptions)
* [`Doopass.Cli.Services.AuthThrottle`](#auththrottle)
* [`Program`](#program)

## `(global)`

### `Program`

Auto-generated public partial Program class for top-level statement apps.

*No documented members.*

## `Doopass.Cli.Api`

### `ApiSession`

An authenticated thin-client session over the REST API (sliding TTL).

* `CreatedUtc` — When the token was issued; the absolute TTL is measured from here.
* `LastSeenUtc` — UTC timestamp of the last authorized call; drives the sliding TTL.
* `Token` — The opaque bearer token that identifies this session.
* `ApiSession(string)` — An authenticated thin-client session over the REST API (sliding TTL).

### `ApiVaultCoordinator`

Hosts the single unlocked `Store` shared by all API tokens.
One store in memory (instead of one per session) removes the race of two
parallel stores writing the same `store.enc`. The coordinator also owns
token registration, the sliding session TTL and the auto-relock when the
last session goes away (logout, TTL expiry).

* `AbsoluteSessionTtl` — Absolute session lifetime (reads `api.absolute_session_ttl_hours`, 8 h by default): an active token must still not live forever.
* `Clock` — Time source; replaced with a fake clock in TTL tests.
* `ServerVersion` — The host version reported to clients.
* `SessionCount` — Number of live sessions (exposed for tests and diagnostics).
* `SessionTtl` — Sliding session TTL (reads `api.session_ttl_minutes` from settings.json when present).
* `DefaultAbsoluteSessionTtl` — Hard cap on a token's lifetime, no matter how active it is.
* `DefaultSessionTtl` — Session lifetime used when `api.session_ttl_minutes` is absent (30 minutes).
* `ApiVaultCoordinator(Settings settings, IVaultChangeNotifier notifier, AuthThrottle throttle)` — Creates the coordinator, reading the optional session TTLs from settings.
  * `settings` — Host-wide settings (store path, backups, API options).
  * `notifier` — Server-wide change notifier used to wake clients.
  * `throttle` — Shared master-password failure counter.
* `Add(string token, string key, string value)` — Adds a pair, persists it and notifies every client.
  * `token` — Caller's bearer token.
  * `key` — New key; must be unique.
  * `value` — Secret value.
* `CreateBackup(string token)` — Creates a dated snapshot of the vault and notifies clients.
  * `token` — Caller's bearer token.
* `DeleteBackup(string token, string backupName)` — Deletes a snapshot by extensionless name and notifies clients.
  * `token` — Caller's bearer token.
  * `backupName` — Snapshot name without the configured extension.
* `GetBackupNames(string)` — Backup names without extension, newest first (token must be live).
* `GetKeys(string token)` — Lists every key; requires a live token.
  *Returns:* Keys in storage order.
  * `token` — Caller's bearer token.
* `GetSettings(string token)` — Reads every settings section; requires a live token.
  *Returns:* Section names paired with a copy of their key/value pairs.
  * `token` — Caller's bearer token.
* `GetState()` — Safe-view of the vault state; reveals no secrets.
* `GetValue(string token, string key)` — Reads one value; requires a live token.
  *Returns:* The stored value.
  * `token` — Caller's bearer token.
  * `key` — Key to read.
* `Init(string)` — Creates a new store, validating the password first. Fails when the store already exists.
* `IsTokenValid(string)` — Validates a hub token WITHOUT sliding its TTL.
* `Login(string)` — Validates the master password and issues a token. When some session has already unlocked the vault, a correct password just adds a token — the live store is kept so parallel clients never race over the file.
* `LoginDelay()` — How long a REST login should pause before checking the password. Shares the host-wide failure counter with the Blazor page; it never refuses, so the ASP.NET limiter remains the only hard cap on REST attempts.
* `Logout(string)` — Expires the given token. When the last session goes away, the vault relocks.
* `PruneExpiredSessions()` — Removes expired sessions; sliding TTL, so any tracked call refreshes `LastSeenUtc`.
* `Reload(string)` — Re-reads the store from disk under the current password.
* `RelockIfIdle_locked()` — When no sessions remain, drop the unlocked store (relock) and tell every hub group.
* `Remove(string token, string key)` — Removes a key and notifies every client.
  * `token` — Caller's bearer token.
  * `key` — Key to remove.
* `RequireExistingBackup_locked(StoreBackup, string)` — Rejects unsafe (path-traversal) and unknown backup names before any file operation — including the safety backup a restore would otherwise write.
* `RestoreBackup(string, string)` — Swaps the store with a backup (safety backup first) and re-reads the unlocked store.
* `RestoreDefaultSettings(string token)` — Rewrites `settings.json` from the embedded defaults and notifies clients.
  * `token` — Caller's bearer token.
* `SetSetting(string token, string section, string key, string value)` — Writes one setting value and notifies clients.
  * `token` — Caller's bearer token.
  * `section` — Existing section name.
  * `key` — Setting key (created when absent).
  * `value` — New value.
* `TestReset()` — Per-test isolation: drops every token, relocks and removes the store file, restoring the clock source. Not part of the production surface.
* `TouchToken(string)` — Validates a token and slides its TTL on authorized calls; null when unknown/expired.
* `Update(string token, string oldKey, string newKey, string newValue)` — Renames a key and replaces its value (remove + add), then notifies clients.
  * `token` — Caller's bearer token.
  * `oldKey` — Existing key.
  * `newKey` — Target key.
  * `newValue` — New value.
* `UseStore``1(string, Store, })` — Validates the token and runs `action` against the coordinator's unlocked store.

### `DiscoveryAnnounce`

What the host announces on the LAN so phones can find it without typing an address.

* `Enabled` — Whether the host should answer discovery probes at all.
* `DiscoveryAnnounce(string, int, bool, string, string)` — What the host announces on the LAN so phones can find it without typing an address.

### `DiscoveryService`

Listens for UDP broadcast probes (`DOOPASS_DISCOVER`, port 7241) and
answers with the host announcement so thin clients can find the vault in the
LAN. Started as a hosted service after the web server itself is up. A bind
failure is logged and disables announcing instead of failing the host.

* `Probe` — `ProbeText` as bytes, for byte-exact comparison.
* `ProbeText` — The exact ASCII datagram a client must send to be answered.
* `DiscoveryService(DiscoveryAnnounce announce)` — Creates the announcer; binds nothing until `StartAsync(CancellationToken)` runs.
  * `announce` — Payload to send back to probes, plus the enable flag.
* `Dispose()` — Cancels the listener and releases the socket and cancellation source.
* `FormatProbe(DiscoveryAnnounce)` — JSON body of the announce reply, exposed for discovery-format tests.
* `StartAsync(CancellationToken cancellationToken)` — Binds UDP 7241 and starts answering probes; a bind failure disables announcing.
  *Returns:* A completed task; listening runs in the background.
  * `cancellationToken` — Startup cancellation token (unused).
* `StopAsync(CancellationToken cancellationToken)` — Stops listening and closes the socket.
  *Returns:* A completed task.
  * `cancellationToken` — Shutdown cancellation token (unused).

### `IVaultChangeNotifier`

Server-side change events raised by sessions mutating the vault. The SignalR
hub relay subscribes to push `vaultChanged` / `backupsChanged` /
`settingsChanged` / `vaultLocked` to API clients, and the Blazor
scopes re-read their state from the same events, so changes made through the
web UI, the REST API or another client become visible everywhere.

* `NotifyBackupsChanged()` — Notifies listeners that the backup list changed.
* `NotifySessionExpired(string token)` — Notifies that one token expired while the vault may stay unlocked.
  * `token` — The expired token.
* `NotifySettingsChanged()` — Notifies listeners that settings changed.
* `NotifyVaultChanged()` — Notifies listeners that vault content changed.
* `NotifyVaultLocked(string token)` — Notifies listeners that a token ended or the vault relocked.
  * `token` — Affected token, or an empty string to notify everyone.
* `BackupsChanged` — Raised after a snapshot was created, restored or deleted.
* `SessionExpired` — One token expired (or was dropped) while other sessions may still hold the vault.
* `SettingsChanged` — Raised after settings were edited or restored to defaults.
* `VaultChanged` — Raised after pairs were added, updated or removed, or the store was reloaded.
* `VaultLocked` — The vault really locked: no session is left, so the store was dropped.

### `NoStoreFilter`

Sets `Cache-Control: no-store` on the whole API group so secret-bearing responses never stick in caches.

* `InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)` — Adds `Cache-Control: no-store` before running the endpoint.
  *Returns:* The endpoint result.
  * `context` — Invocation context of the endpoint.
  * `next` — The next filter or the endpoint itself.

### `RequireVaultTokenFilter`

Endpoint filter enforcing `Authorization: Bearer <token>` against the coordinator.

* `InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)` — Rejects requests without a live bearer token and slides the token's TTL.
  *Returns:* 401 JSON on failure, otherwise the endpoint result.
  * `context` — Invocation context of the endpoint.
  * `next` — The next filter or the endpoint itself.

### `SessionJanitor`

Drops sessions whose sliding or absolute TTL ran out, even when no client
makes another request. Without this the decrypted store would stay in host
memory indefinitely after the last browser tab or phone closed.

* `Interval` — How often expired tokens are swept (default 30 seconds).
* `SessionJanitor(ApiVaultCoordinator)` — Drops sessions whose sliding or absolute TTL ran out, even when no client makes another request. Without this the decrypted store would stay in host memory indefinitely after the last browser tab or phone closed.

### `VaultAuth`

Shared helpers for the vault REST API.

* `AuthorizationSchemes` — The only accepted authorization scheme (`Bearer`).
* `TokenItemKey` — Key under which the validated token is stored in `HttpContext.Items`.
* `AddVaultRateLimiter(IServiceCollection)` — Fixed-window login limiter, 5 requests per minute per remote IP.
* `ParseBearerToken(HttpContext context)` — Extracts the bearer token from the request headers.
  *Returns:* The token, or `null` when the header is missing or malformed.
  * `context` — Current HTTP context.
* `Unauthorized()` — Builds the canonical 401 JSON response.
  *Returns:* A JSON result carrying `{"error":"Session expired or unauthorized."}`.

### `VaultChangeNotifier`

Default server-wide notifier; subscribers are wired once at startup.

*No documented members.*

### `VaultEndpoints`

REST surface of the vault host for thin clients. Auth is
`Authorization: Bearer <token>` everywhere except
`GET /api/vault/state`. Keys and backup names travel as query
parameters or JSON bodies — never path segments — because they may contain
'/', '=' and unicode. All endpoints in the group get
`Cache-Control: no-store`; errors are always
`{ "error": "<message>" }`.

* `CanonicalStatus(string)` — Auth outcomes carry their message in the coordinator result; a missing store is a conflict. Failures never turn into a refusal any more — they only delay the attempt — so there is no "too many attempts" status here.
* `GuardAsync(IResult}})` — Async sibling of `Guard(IResult>)` for handlers that wait before touching the store.
* `MapDoopassApi(IEndpointRouteBuilder app)` — Maps the whole `/api` group (auth, vault, backups, settings) onto the pipeline.
  *Returns:* The group, for further conventions if needed.
  * `app` — Endpoint route builder of the web host.
* `Unexpected(Exception)` — Anything we did not plan for may carry file-system paths, .NET type names or other internals; the client gets a fixed message and the details go to the log only.

### `VaultHub`

Push channel for API clients. The token travels with the standard
`access_token` query parameter (SignalR's `WithAccessTokenProvider`
convention); on connect the token is validated and each connection joins its
own token group. Events are camelCase: `vaultChanged`, `backupsChanged`,
`settingsChanged`, `vaultLocked`.

* `VaultHub(ApiVaultCoordinator)` — Push channel for API clients. The token travels with the standard `access_token` query parameter (SignalR's `WithAccessTokenProvider` convention); on connect the token is validated and each connection joins its own token group. Events are camelCase: `vaultChanged`, `backupsChanged`, `settingsChanged`, `vaultLocked`.
* `Group(string)` — Group name carrying a vault token; the leading prefix avoids id collisions.
* `OnConnectedAsync()` — Validates the `access_token` query parameter and joins the token's group.
  *Returns:* A task that completes once the group membership is registered.

### `VaultHubRelay`

Relays server-side vault changes to connected hub clients.

* `VaultHubRelay(VaultHub}, IVaultChangeNotifier)` — Relays server-side vault changes to connected hub clients.
* `ExecuteAsync(CancellationToken stoppingToken)` — Subscribes to the notifier and starts relaying events to hub clients.
  *Returns:* A completed task; the service is event-driven.
  * `stoppingToken` — Host shutdown token.
* `FireSessionExpired(CancellationToken, string)` — A single token expired: only its own hub group is told.

### `WebHostFactory`

Builds (and runs) the Doopass web host: static Blazor WebAssembly UI, REST API under
/api, the vault SignalR hub and LAN discovery. Split from `Program.cs`
so integration tests can build the same pipeline on a random port.

* `SchemeProperty` — App property carrying the scheme the host actually serves.
* `BuildWebHost(WebHostOptions)` — Builds the web host (static WASM UI, REST API, SignalR hub and discovery) without starting it, so tests can run the real pipeline on a free port.
* `ExtractEmbeddedWasmBundle()` — Extracts the embedded WASM bundle to a version-addressed temp directory and returns its path. A marker file makes repeat starts cheap; failures fall back to the SDK static web assets instead of taking the host down.
* `MapSpaFallback(WebApplication, IFileProvider)` — Serves index.html for client-side routes, but leaves API, hub, PWA and file-shaped paths as real 404s instead of masking them with the SPA shell.
* `RunWebHost(WebHostOptions)` — Runs a built host to completion (SIGTERM/CTRL-C handled by the runtime).
* `ShouldSendHsts(string)` — HSTS may only be pinned for a real network host. If a browser remembers it for localhost, the next plain `http://localhost` run is upgraded to https against a certificate that run does not even serve.

### `WebHostOptions`

Options for building the web host: ports, TLS, announce and browser behavior.

* `Announce` — Force LAN discovery announce even without --lan.
* `Host` — Bind host override (tests bind 127.0.0.1); when null LAN binds 0.0.0.0, otherwise localhost.
* `Lan` — Bind all interfaces instead of loopback and enable LAN discovery.
* `OpenBrowser` — Open the default browser on startup.
* `Port` — TCP port for the web UI and the API (default `7240`).
* `TlsPassword` — Password for `TlsPfx`; null when the file is unprotected.
* `TlsPfx` — Path to the server TLS certificate (.pfx); null keeps plain HTTP.

## `Doopass.Cli.Services`

### `AuthThrottle`

Shared master-password throttle for every login path (REST API and the Blazor
page). One instance per host, so the failure counter is global rather than
per client: a LAN attacker who keeps guessing can no longer lock the owner
out. Instead of refusing attempts, the throttle inserts a growing pause
before the password check once five failures fell inside the sliding minute
(1 s, 2 s, 4 s, at most 5 s) and a successful login clears the count. The
owner can therefore always log in; raw REST request volume is still capped by
the ASP.NET per-IP limiter, which sees the network but not Blazor circuits.

* `Clock` — Time source; replaced with a fake clock in tests.
* `DelayHook` — Performs the pause computed by `DelayBeforeAttempt`; replaced in tests so they record the delays instead of waiting for them.
* `DelayStartsAfter` — Number of failures inside the window after which attempts start to be delayed.
* `MaxDelay` — Upper bound on the pause inserted before a login attempt.
* `DelayBeforeAttempt()` — How long to pause before checking the password. Never refuses an attempt: with five failures in the sliding window the pause grows 1 s → 2 s → 4 s and stays at `MaxDelay` after that, so the owner always gets in.
* `RegisterFailure()` — Records one failed master-password attempt.
* `RegisterSuccess()` — Clears the failure history after a successful login.
* `Reset()` — Forgets every failure (test isolation and an explicit admin reset).
