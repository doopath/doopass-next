# Application: REST API and Hub

The private API that the Android client uses. It is served by `doopass serve`
on the same port as the web UI; there is no separate listener.

* Base path: `/api`
* Auth: `Authorization: Bearer <token>` on everything except
  `GET /api/vault/state`
* Responses: JSON; errors are `{"error": "<message>"}`
* Every `/api` response: `Cache-Control: no-store`
* Hub: `/hubs/vault` (SignalR), token as `access_token` query parameter

## Components

| Type | Role |
| ---- | ---- |
| `ApiVaultCoordinator` (singleton) | owns the unlocked `Store`, token dictionary, sliding TTL, backup/settings operations |
| `VaultEndpoints` | maps the routes, validates bodies, maps exceptions to status codes |
| `RequireVaultTokenFilter` | parses and validates the bearer token, slides its TTL, stores it in `HttpContext.Items` |
| `NoStoreFilter` | sets `Cache-Control: no-store` for the whole group |
| `VaultAuth` | token parsing regex, 401 helper, fixed-window rate limiter |
| `VaultHub` | SignalR hub; validates the token on connect, joins a per-token group |
| `VaultHubRelay` | hosted service forwarding `IVaultChangeNotifier` events to all/grouped clients |
| `VaultChangeNotifier` | server-wide event source also consumed by Blazor circuits |

## Coordinator semantics

```csharp
ApiVaultCoordinator(Settings settings, IVaultChangeNotifier notifier)
  Clock            // injectable for TTL tests
  SessionTtl       // default 30 min, from api.session_ttl_minutes
  ServerVersion    // Doopass.Core.Constants.Version
  SessionCount     // diagnostics
```

| Operation | Behaviour |
| --------- | --------- |
| `Login(password)` | prunes expired tokens; 409 when no store file; verifies the password by constructing a `Store`; keeps the first unlocked store instance; adds a token |
| `Init(password)` | prunes; 409 when a store exists; validates the password; creates the store; adds a token |
| `Logout(token)` | removes the token; relocks when none remain |
| `TouchToken(token)` | prunes, then slides `LastSeenUtc`; returns `null` for unknown/expired tokens |
| `IsTokenValid(token)` | prunes, checks membership, **does not** slide (used by the hub) |
| `GetKeys/GetValue/Add/Update/Remove` | token check under `_gate`, then the `Store` call; mutations notify listeners |
| `Reload` | re-creates the `Store` from disk (same password) |
| `GetBackupNames/CreateBackup/RestoreBackup/DeleteBackup` | token check; restore also reloads the store and notifies vault + backups |
| `GetSettings/SetSetting/RestoreDefaultSettings` | token check; unknown sections are rejected with `PropertyDoesNotExistError` |
| `PruneExpiredSessions` | explicit cleanup (also implicit on every token operation) |

All state transitions happen under a single private lock, so two API tokens
cannot interleave a read-modify-write on the store.

## Endpoints

### Anonymous

| Method | Path | Response |
| ------ | ---- | -------- |
| `GET` | `/api/vault/state` | `{storeExists, unlocked, serverVersion, certFingerprint}` |
| `POST` | `/api/auth/login` | `{token, serverVersion}` — rate limited |
| `POST` | `/api/auth/init` | `{token, serverVersion}` — rate limited |

### Authenticated

| Method | Path | Body / query | Success |
| ------ | ---- | ------------ | ------- |
| `POST` | `/api/auth/logout` | — | 204 |
| `GET` | `/api/vault/keys` | — | `{keys:[...]}` |
| `GET` | `/api/vault/value` | `?key=` | `{value:"..."}` |
| `POST` | `/api/vault/pairs` | `{key, value}` | 204 |
| `PUT` | `/api/vault/pairs` | `{oldKey, newKey, newValue}` | 204 |
| `DELETE` | `/api/vault/pairs` | `?key=` | 204 |
| `POST` | `/api/vault/reload` | — | 204 |
| `GET` | `/api/backups` | — | `{names:[...]}` (extensionless) |
| `POST` | `/api/backups` | — | 204 |
| `POST` | `/api/backups/restore` | `?name=` | 204 |
| `DELETE` | `/api/backups` | `?name=` | 204 |
| `GET` | `/api/settings` | — | `{sections:[{name, pairs}]}` |
| `PUT` | `/api/settings/{section}/{key}` | `{value}` | 204 |
| `POST` | `/api/settings/restore-defaults` | — | 204 |

Keys, backup names and setting values are carried in query strings or JSON
bodies, never in path segments, because they can contain `/`, `=` and unicode.
`/api/settings/{section}/{key}` uses `Uri.EscapeDataString` on the client.

## Status codes

| Situation | Code | Body |
| --------- | ---- | ---- |
| Missing `password` / `key` / `oldKey` / `newKey` | 400 | validator or "…is required." message |
| Wrong password, unknown/expired token, bad `Authorization` header | 401 | "Incorrect password!" / "Session expired or unauthorized." |
| Missing key, backup or settings section | 404 | the exception message |
| Duplicate key | 409 | "This property (x) already exists!" |
| Store missing on login, store already exists on init | 409 | matching message |
| More than 5 auth requests/minute per IP | 429 | "Too many authentication attempts. Slow down." |
| Anything unexpected | 500 | exception message |

`VaultEndpoints.MapStoreError` performs the mapping; auth outcomes are mapped by
`CanonicalStatus` from the coordinator's message. Unknown backup names and
settings sections are pre-checked so the client gets 404 rather than 500.

## Rate limiting

Fixed window, keyed by `HttpContext.Connection.RemoteIpAddress` (`"unknown"`
when null):

* permit limit 5, queue 0, window 1 minute;
* applies to `POST /api/auth/login` and `POST /api/auth/init`;
* rejection is 429 with `Cache-Control: no-store` and a JSON error body.

Behind a reverse proxy all clients share the proxy's IP and therefore one
bucket — the host is designed to be exposed directly, not through a proxy.

## SignalR hub

```csharp
// server
public override Task OnConnectedAsync()
{
    var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
    if (string.IsNullOrEmpty(token) || !coordinator.IsTokenValid(token)) { Context.Abort(); ... }
    return Groups.AddToGroupAsync(Context.ConnectionId, Group(token));  // "doopass:<token>"
}
```

Events (server → client, no arguments):

| Event | Scope | Raised by |
| ----- | ----- | --------- |
| `vaultChanged` | all clients | pair add/update/remove, store reload after restore |
| `backupsChanged` | all clients | backup create/restore/delete |
| `settingsChanged` | all clients | setting edit, restore defaults |
| `vaultLocked` | token group, or all when no sessions remain | logout, TTL expiry, idle relock |

A client that receives an event simply re-reads what it displays. Tokens are
validated only at connect time; a token that expires mid-connection is not
kicked, but its next REST call returns 401 and the client treats that as a lock.

## Client example

```bash
# state (anonymous)
curl -s http://localhost:7240/api/vault/state

# login
TOKEN=$(curl -s -X POST http://localhost:7240/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"password":"correct horse"}' | jq -r .token)

# add, list, read
curl -s -X POST http://localhost:7240/api/vault/pairs \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"key":"github","value":"hunter2"}'
curl -s http://localhost:7240/api/vault/keys -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:7240/api/vault/value?key=github" -H "Authorization: Bearer $TOKEN"

# backups
curl -s -X POST http://localhost:7240/api/backups -H "Authorization: Bearer $TOKEN"
curl -s http://localhost:7240/api/backups -H "Authorization: Bearer $TOKEN"
```

Interactive docs for manual testing: `curl`, `httpie`, or the smoke scripts
`scripts/smoke-api.ps1` / `scripts/smoke-tls.ps1`.

## Error mapping on the client

`RemoteVaultSession.EnsureSuccessAsync` converts status codes into the same
exceptions the server-side session would throw:

| Status | Client exception | Effect |
| ------ | ---------------- | ------ |
| 401 | `UnauthorizedAccessException` + token cleared + `VaultLocked` raised | back to login |
| 404 | `PropertyDoesNotExistError` | normal "not found" handling |
| 409 | `PropertyAlreadyExistError` | duplicate-key message |
| 429 | `InvalidOperationException` | message shown |
| other | `HttpRequestException` | toast with the status and message |

This is what allows one set of Razor pages to run unchanged against a local
store or a remote host.

## Extending the API

1. Add the operation to `ApiVaultCoordinator` (token check first, notify last).
2. Map it in `VaultEndpoints` inside the `secure` group; validate the body and
   return `Results.NoContent()` or a camelCase JSON object.
3. Add the client method to `RemoteVaultSession`.
4. Add an integration test to `Doopass.Api.Tests` (the fixture starts the real
   host on a free port with an isolated `DOOPASS_HOME`).
5. Document the endpoint here, in [API Reference](API-Reference.md) and in
   `Data-Formats.md` if the wire shape is new.
