# API Reference (REST, Hub, Discovery)

Complete surface exposed by `doopass serve`. Implementation details and rationale
are in [Application: REST API](Application-REST-API.md).

## Conventions

* Base URL: `http://localhost:7240` (or `https://<host>:<port>` with `--tls`).
* All API responses: `Cache-Control: no-store`.
* Request/response bodies: JSON, camelCase properties.
* Errors: `{"error": "<message>"}` with a 4xx/5xx status.
* Auth: `Authorization: Bearer <token>`; token from `/api/auth/login` or
  `/api/auth/init`.
* Token lifetime: sliding, default 30 minutes (`api.session_ttl_minutes`).

## Authentication

### `POST /api/auth/login`

Anonymous, rate-limited (5/min per IP).

```json
{ "password": "master password" }
```

| Result | Status | Body |
| ------ | ------ | ---- |
| success | 200 | `{"token":"…","serverVersion":"5.0.0"}` |
| missing password | 400 | `{"error":"Password is required!"}` |
| wrong password | 401 | `{"error":"Incorrect password!"}` |
| no store | 409 | `{"error":"Store is not initialized. Please initialize it first."}` |
| rate limited | 429 | `{"error":"Too many authentication attempts. Slow down."}` |

### `POST /api/auth/init`

Anonymous, rate-limited. Creates the vault; fails when one exists.

| Result | Status | Body |
| ------ | ------ | ---- |
| success | 200 | `{"token":"…","serverVersion":"5.0.0"}` |
| invalid password (validator message) | 400 | `{"error":"Password is too small!"}` etc. |
| store exists | 409 | `{"error":"Store already exists!"}` |

### `POST /api/auth/logout`

Authenticated. Removes the token; relocks the vault when it was the last one.

`204 No Content` in every case (unknown token included, since auth already ran).

## Vault state and pairs

### `GET /api/vault/state`

Anonymous.

```json
{
  "storeExists": true,
  "unlocked": false,
  "serverVersion": "5.0.0",
  "certFingerprint": null
}
```

`certFingerprint` is the lowercase SHA-256 of the TLS certificate, or `null` on
plain HTTP.

### `GET /api/vault/keys`

```json
{ "keys": ["github", "router"] }
```

### `GET /api/vault/value?key=<key>`

```json
{ "value": "hunter2" }
```

404 when the key is missing. The key is URL-encoded by the client
(`Uri.EscapeDataString`).

### `POST /api/vault/pairs`

```json
{ "key": "github", "value": "hunter2" }
```

| Result | Status |
| ------ | ------ |
| created | 204 |
| empty key | 400 `{"error":"Key is required."}` |
| duplicate key | 409 `{"error":"This property (github) already exists!"}` |

### `PUT /api/vault/pairs`

```json
{ "oldKey": "github", "newKey": "git", "newValue": "s3cret" }
```

| Result | Status |
| ------ | ------ |
| updated | 204 |
| missing `oldKey`/`newKey` | 400 `{"error":"Both oldKey and newKey are required."}` |
| old key missing | 404 |
| new key already exists | 409 (the old key has already been removed — see [Known Limitations](Known-Limitations.md#rename-can-lose-the-old-key)) |

### `DELETE /api/vault/pairs?key=<key>`

204 on success, 404 when the key does not exist.

### `POST /api/vault/reload`

204. Re-reads `store.enc` from disk with the current password. Used when an
external process (for example the TUI) changed the file.

## Backups

Names are **extensionless** everywhere in the API; the host appends the
configured extension.

| Method | Path | Result |
| ------ | ---- | ------ |
| `GET` | `/api/backups` | `{"names":["26_10_2026_store_1"]}` — ordinal descending |
| `POST` | `/api/backups` | 204 — creates a dated snapshot |
| `POST` | `/api/backups/restore?name=<name>` | 204 — safety backup, then overwrite, then reload |
| `DELETE` | `/api/backups?name=<name>` | 204; 404 when it does not exist |

A restore raises both `backupsChanged` and `vaultChanged` on the hub.

## Settings

| Method | Path | Body | Result |
| ------ | ---- | ---- | ------ |
| `GET` | `/api/settings` | — | `{"sections":[{"name":"local","pairs":{…}}]}` |
| `PUT` | `/api/settings/{section}/{key}` | `{"value":"…"}` | 204; 404 for an unknown section |
| `POST` | `/api/settings/restore-defaults` | — | 204 — rewrites `settings.json` from defaults |

Section and key are URL-encoded. Values are always strings.

## Status code summary

| Code | Meaning here |
| ---- | ------------ |
| 200 | successful read/login/init |
| 204 | successful mutation |
| 400 | validation failure (missing password/key/oldKey/newKey, password rules) |
| 401 | wrong password, invalid/expired token, malformed `Authorization` header |
| 404 | unknown key, backup, or settings section |
| 409 | duplicate key, store exists, store missing |
| 429 | more than 5 auth attempts per minute from one IP |
| 500 | unexpected server error (message included) |

## SignalR hub

URL: `/hubs/vault?access_token=<token>`

Invalid or missing token → the connection is aborted.

Server-to-client methods, no arguments:

| Method | Trigger |
| ------ | ------- |
| `vaultChanged` | pair add/update/remove, store reload after a restore |
| `backupsChanged` | snapshot created/restored/deleted |
| `settingsChanged` | settings edited or restored |
| `vaultLocked` | caller's session ended, or the vault relocked for everyone |

Clients re-read state after an event; nothing is pushed in the payload.

Minimal client (C#):

```csharp
var hub = new HubConnectionBuilder()
    .WithUrl($"{host}/hubs/vault", o => o.AccessTokenProvider = () => Task.FromResult(token))
    .WithAutomaticReconnect()
    .Build();

hub.On("vaultChanged", () => ReloadAsync());
hub.On("vaultLocked", () => SignOutAsync());
await hub.StartAsync();
```

## LAN discovery

UDP port 7241. Send exactly `DOOPASS_DISCOVER`; receive:

```json
{
  "service": "doopass",
  "name": "DESKTOP-7F3",
  "port": 7240,
  "tls": true,
  "fingerprint": "d3894b6e…c8e50f",
  "version": "5.0.0"
}
```

Probe with a shell one-liner:

```bash
echo -n DOOPASS_DISCOVER | nc -u -w1 -b 255.255.255.255 7241
```

## Embedded web routes

| Route | Content |
| ----- | ------- |
| `/`, `/init` | login / init pages |
| `/store`, `/backups`, `/settings` | authenticated pages |
| `/app.css`, `/theme.js`, `/app.js` | UI assets (revalidated) |
| `/_framework/blazor.web.js` | Blazor runtime (1 h cache) |
| `/pwa/*` | manifest, service worker, icons, favicons |
| `/Error`, `/not-found` | error pages |

## Related pages

* [Data Formats](Data-Formats.md) — exact JSON shapes and the vault format
* [Business Rules](Business-Rules.md) — why each status is what it is
* [Generated API docs](API-Reference-Core.md) — XML-doc reference for the types
