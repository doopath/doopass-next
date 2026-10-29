# API Reference — doopass

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `doopass` · Source: `src/Doopass.Cli/bin/Debug/net10.0/doopass.xml`

## Types

* [`Doopass.Cli.Api.ApiSession`](#apisession)
* [`Doopass.Cli.Api.ApiVaultCoordinator`](#apivaultcoordinator)
* [`Doopass.Cli.Api.DiscoveryAnnounce`](#discoveryannounce)
* [`Doopass.Cli.Api.DiscoveryService`](#discoveryservice)
* [`Doopass.Cli.Api.IVaultChangeNotifier`](#ivaultchangenotifier)
* [`Doopass.Cli.Api.NoStoreFilter`](#nostorefilter)
* [`Doopass.Cli.Api.RequireVaultTokenFilter`](#requirevaulttokenfilter)
* [`Doopass.Cli.Api.VaultAuth`](#vaultauth)
* [`Doopass.Cli.Api.VaultChangeNotifier`](#vaultchangenotifier)
* [`Doopass.Cli.Api.VaultEndpoints`](#vaultendpoints)
* [`Doopass.Cli.Api.VaultHub`](#vaulthub)
* [`Doopass.Cli.Api.VaultHubRelay`](#vaulthubrelay)
* [`Doopass.Cli.Api.WebHostFactory`](#webhostfactory)
* [`Doopass.Cli.Api.WebHostOptions`](#webhostoptions)
* [`Doopass.Cli.Services.DoopassSession`](#doopasssession)
* [`Program`](#program)

## `(global)`

### `Program`

Auto-generated public partial Program class for top-level statement apps.

*No documented members.*

## `Doopass.Cli.Api`

### `ApiSession`

An authenticated thin-client session over the REST API (sliding TTL).

* `LastSeenUtc` — UTC timestamp of the last authorized call; drives the sliding TTL.
* `Token` — The opaque bearer token that identifies this session.
* `ApiSession(string)` — An authenticated thin-client session over the REST API (sliding TTL).

### `ApiVaultCoordinator`

Hosts the single unlocked `Store` shared by all API tokens.
One store in memory (instead of one per session) removes the race of two
parallel stores writing the same `store.enc`. The coordinator also owns
token registration, the sliding session TTL and the auto-relock when the
last session goes away (logout, TTL expiry).

* `Clock` — Time source; replaced with a fake clock in TTL tests.
* `ServerVersion` — The host version reported to clients.
* `SessionCount` — Number of live sessions (exposed for tests and diagnostics).
* `SessionTtl` — Sliding session TTL (reads `api.session_ttl_minutes` from settings.json when present).
* `DefaultSessionTtl` — Session lifetime used when `api.session_ttl_minutes` is absent (30 minutes).
* `ApiVaultCoordinator(Settings settings, IVaultChangeNotifier notifier)` — Creates the coordinator, reading the optional session TTL from settings.
  * `settings` — Host-wide settings (store path, backups, API options).
  * `notifier` — Server-wide change notifier used to wake clients.
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
* `Logout(string)` — Expires the given token. When the last session goes away, the vault relocks.
* `PruneExpiredSessions()` — Removes expired sessions; sliding TTL, so any tracked call refreshes `LastSeenUtc`.
* `Reload(string)` — Re-reads the store from disk under the current password.
* `RelockIfIdle_locked()` — When no sessions remain, drop the unlocked store (relock) and tell every hub group.
* `Remove(string token, string key)` — Removes a key and notifies every client.
  * `token` — Caller's bearer token.
  * `key` — Key to remove.
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
* `NotifySettingsChanged()` — Notifies listeners that settings changed.
* `NotifyVaultChanged()` — Notifies listeners that vault content changed.
* `NotifyVaultLocked(string token)` — Notifies listeners that a token ended or the vault relocked.
  * `token` — Affected token, or an empty string to notify everyone.
* `BackupsChanged` — Raised after a snapshot was created, restored or deleted.
* `SettingsChanged` — Raised after settings were edited or restored to defaults.
* `VaultChanged` — Raised after pairs were added, updated or removed, or the store was reloaded.
* `VaultLocked` — Raised when a session ends or the vault relocks. The argument is the affected token, or an empty string when no sessions remain (broadcast).

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

* `CanonicalStatus(string)` — Auth outcomes carry their message in the coordinator result; missing store is a conflict.
* `MapDoopassApi(IEndpointRouteBuilder app)` — Maps the whole `/api` group (auth, vault, backups, settings) onto the pipeline.
  *Returns:* The group, for further conventions if needed.
  * `app` — Endpoint route builder of the web host.

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

### `WebHostFactory`

Builds (and runs) the Doopass web host: Blazor Server UI, REST API under
/api, the vault SignalR hub and LAN discovery. Split from `Program.cs`
so integration tests can build the same pipeline on a random port.

* `BuildWebHost(WebHostOptions options)` — Builds the web host (Blazor UI, REST API, SignalR hub and discovery) without starting it, so tests can run the real pipeline on a free port.
  *Returns:* A configured, not yet started application.
  * `options` — Port, bind address, TLS and discovery options.
  *Throws* `InvalidOperationException`: The TLS certificate is missing or cannot be loaded.
* `RunWebHost(WebHostOptions)` — Runs a built host to completion (SIGTERM/CTRL-C handled by the runtime).
* `ValidateTlsCertificate(string, string)` — Checks the TLS certificate exists and is loadable; returns its SHA-256 fingerprint.

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

### `DoopassSession`

Per-circuit session holding the currently unlocked store. The store only
lives in server memory while the user is logged in. Every mutation raises
`IVaultChangeNotifier` events, which fan out to SignalR hub
clients (API tokens) and back into this circuit's `VaultEventRelay`
so open pages refresh.

* `Backups` — Backup helper bound to this vault.
* `IsUnlocked` — True while this circuit holds a decrypted store.
* `Settings` — The host-wide settings object (shared by every circuit).
* `StoreExists` — True when the encrypted store file exists on disk.
* `DoopassSession(Settings settings, IVaultChangeNotifier notifier, VaultEventRelay relay)` — Creates the session and wires the server-wide change events into this circuit's relay (when both are supplied by the host).
  * `settings` — Host-wide settings.
  * `notifier` — Optional server-wide notifier.
  * `relay` — Optional per-circuit relay.
* `Add(string key, string value)` — Adds a pair and notifies every client.
  * `key` — New key; must be unique.
  * `value` — Secret value.
* `DeleteBackupAsync(string backupName, CancellationToken ct = default)` — Deletes a backup by extensionless name.
  * `backupName` — Extensionless snapshot name.
  * `ct` — Cancellation token.
* `Dispose()` — Detaches this circuit from the server-wide notifier.
* `GetBackupNamesAsync(CancellationToken ct = default)` — Lists backup names without extension, newest first.
  *Returns:* Extensionless snapshot names.
  * `ct` — Cancellation token.
* `GetKeys()` — Lists every key of the unlocked store (empty when locked).
  *Returns:* Keys in storage order.
* `GetValue(string key)` — Reads one secret value from the unlocked store.
  *Returns:* The stored value.
  * `key` — Key to read.
* `Init(string password)` — Creates a new store with the given password and unlocks it.
  *Returns:* Success flag and a user-facing message.
  * `password` — The new master password.
* `Login(string password)` — Tries to unlock the store with the given password.
  *Returns:* Success flag and a user-facing message.
  * `password` — The master password.
* `Logout()` — Locks this circuit: the decrypted store and the password are dropped.
* `Reload()` — Re-reads the store from disk (e.g. after restoring a backup).
* `Remove(string key)` — Removes a key and notifies every client.
  * `key` — Key to remove.
* `RestoreBackupAsync(string backupName, CancellationToken ct = default)` — Restores a backup by extensionless name; a safety backup is written first, then the session re-reads the store.
  * `backupName` — Extensionless snapshot name.
  * `ct` — Cancellation token.
* `Update(string oldKey, string newKey, string newValue)` — Renames a key and replaces its value, then notifies every client.
  * `oldKey` — Existing key.
  * `newKey` — Target key.
  * `newValue` — New value.
