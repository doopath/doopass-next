# Configuration Reference

Every knob Doopass exposes: environment variables, CLI flags, `settings.json`
keys, and the constants that are compiled in.

## Environment variables

| Variable | Default | Effect |
| -------- | ------- | ------ |
| `DOOPASS_HOME` | `~/doopass` | application root; `appdata` is created inside it |
| `HOME` | platform profile | fallback home directory when the OS reports none |
| `ANDROID_HOME` / `ANDROID_SDK_ROOT` | — | Android SDK location for building the phone app |
| `JAVA_HOME` | — | JDK used by the Android build |

## Paths

```
<DOOPASS_HOME or ~/doopass>/
└── appdata/
    ├── store.enc         encrypted vault
    ├── settings.json     configuration (below)
    ├── backups/          dated snapshots
    └── doopass.log       log (+ doopass.log.1)
```

## CLI

```
doopass                                        terminal UI
doopass serve [--port N] [--no-browser] [--lan] [--announce]
              [--tls <pfx>] [--tls-pass <password>]
```

| Flag | Alias | Default | Notes |
| ---- | ----- | ------- | ----- |
| `serve` | `--serve`, `--web` | — | first argument selects the web host |
| `--port N` | `-p N` | `7240` | unparsable value keeps the default |
| `--no-browser` | — | browser opens | the host still prints the URL |
| `--lan` | `--network` | loopback | binds `0.0.0.0` and enables discovery |
| `--announce` | — | off | discovery without `--lan` |
| `--tls <pfx>` | `--tls-pfx` | none | HTTPS, binds all interfaces |
| `--tls-pass <password>` | — | none | `.pfx` password |

Unknown flags and flags missing a value are ignored — there is no argument
validation error path.

## `settings.json`

```json
{
  "local": {
    "version": "5.0.0",
    "backup_extension": ".enc.bak",
    "store_file": "APPDATA/store.enc",
    "log_file": "APPDATA/doopass.log",
    "backups": "APPDATA/backups",
    "default_config": "APPDATA/default_config.json"
  },
  "api": {
    "session_ttl_minutes": "30",
    "announce": "false",
    "tls_pfx": "/home/user/doopass/doopass.pfx",
    "tls_pass": "CHANGE-ME"
  }
}
```

### `[local]`

| Key | Default | Meaning |
| --- | ------- | ------- |
| `version` | `5.0.0` | informational; not checked at runtime |
| `backup_extension` | `.enc.bak` | suffix for snapshots |
| `store_file` | `APPDATA/store.enc` | vault path |
| `log_file` | `APPDATA/doopass.log` | log path (`AppLogger.Initialize`) |
| `backups` | `APPDATA/backups` | snapshot directory |
| `default_config` | `APPDATA/default_config.json` | reserved/unused by current code |

### `[api]` (optional)

| Key | Default | Read by | Meaning |
| --- | ------- | ------- | ------- |
| `session_ttl_minutes` | `30` | `ApiVaultCoordinator` | sliding token TTL; must parse as a positive `double` |
| `announce` | `false` | `WebHostFactory` | `"true"` enables discovery |
| `tls_pfx` | none | `WebHostFactory` | certificate path when `--tls` is absent |
| `tls_pass` | none | `WebHostFactory` | certificate password, **plain text** |

The section is not created by default. "Restore defaults" removes it.

### Path placeholders

Any value containing `APPDATA` is rewritten on load: the text from `APPDATA`
onwards is treated as a relative path under `appdata`. Values that start with the
real data directory are converted back on save. Absolute paths outside that
directory are stored as-is.

### Legacy section

`remote` (from the Python version) is dropped on load and the file rewritten.

### Editing rules

* Values are always strings.
* Unknown keys inside a known section are preserved and editable.
* Unknown sections are preserved; the REST API refuses to write into one that
  does not exist (404).
* Missing keys used by accessors (`store_file`, `log_file`, `backups`,
  `backup_extension`) throw `KeyNotFoundException` — keep `[local]` intact.

## Compiled-in constants

| Constant | Value | Where |
| -------- | ----- | ----- |
| `Constants.Version` | `5.0.0` | `Doopass.Core/Constants.cs` |
| `Constants.DefaultIterations` / `Store.DefaultIterations` | `100 000` | key derivation when writing |
| `Constants.StoreBackupExtension` | `.enc.bak` | fallback when settings lack the key |
| API port default | `7240` | `WebHostOptions.Port` |
| Discovery port | `7241` | `DiscoveryService`, `HostDiscovery.AnnouncePort` |
| Discovery probe | `DOOPASS_DISCOVER` | both sides |
| API session TTL default | `30 min` | `ApiVaultCoordinator.DefaultSessionTtl` |
| Login rate limit | 5/min per IP | `VaultAuth.AddVaultRateLimiter` |
| Log rotation | 5 MB | `AppLogger` |
| Client HTTP timeouts | 4 s state, 8 s auth, 20 s general, 6 s discovery probe | `RemoteVaultSession`, `Connect.razor` |

## Browser and phone state

| Store | Key | Values |
| ----- | --- | ------ |
| `localStorage` | `doopass-theme` | `dark` / `light` |
| Android `SecureStorage` | `doopass.host`, `doopass.pin`, `doopass.token` | host URL, SHA-256 pin, bearer token |

## Precedence summary

| Setting | Order (first wins) |
| ------- | ------------------ |
| TLS certificate | `--tls` → `api.tls_pfx` |
| TLS password | `--tls-pass` → `api.tls_pass` |
| Bind address | TLS → all interfaces; `--lan` → `0.0.0.0`; else `localhost` |
| Discovery | `--lan` / `--announce` → `api.announce` |
| Store/log/backup paths | `settings.json` → `DOOPASS_HOME`/defaults |
| Session TTL | `api.session_ttl_minutes` → 30 min |
