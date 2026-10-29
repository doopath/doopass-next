# Feature: Settings

Application configuration stored in `settings.json` next to the vault. Settings
are host-wide: every client edits the same file.

## What the user can do

| Action | Web UI | TUI | REST API |
| ------ | ------ | --- | -------- |
| View all sections | `/settings` cards | settings screen | `GET /api/settings` |
| Change one value | edit icon → modal | `Enter` on a row | `PUT /api/settings/{section}/{key}` |
| Restore defaults | "Restore defaults" → confirm | `0` → confirm | `POST /api/settings/restore-defaults` |

The web page also hosts the **Install as app** card for the PWA (see
[Feature: PWA](Feature-PWA.md)).

## File and defaults

```json
{
  "local": {
    "version": "5.0.0",
    "backup_extension": ".enc.bak",
    "store_file": "APPDATA/store.enc",
    "log_file": "APPDATA/doopass.log",
    "backups": "APPDATA/backups",
    "default_config": "APPDATA/default_config.json"
  }
}
```

* Defaults are embedded in `Doopass.Core` (`Configuration/DefaultConfig.json`)
  and written on first run.
* The file is JSON (`section → key → string`), indented, UTF-8 without BOM.
* Values containing the data directory are stored with an `APPDATA/` prefix so the
  file stays portable; it is expanded on load and re-collapsed on save.
* The obsolete Python section `remote` is dropped on load and the file rewritten.
* Unknown sections are preserved. `api` is the documented optional section:

| Key | Used by | Meaning |
| --- | ------- | ------- |
| `tls_pfx` | web host | path to the PKCS#12 certificate (plain-text password lives in `tls_pass`) |
| `tls_pass` | web host | certificate password |
| `session_ttl_minutes` | API coordinator | sliding session TTL, double > 0 |
| `announce` | web host | `"true"` enables UDP discovery without `--lan` |

## Editing rules

| Layer | Rule |
| ----- | ---- |
| Web | Every row is editable; saving persists immediately and raises `SettingsChanged` |
| TUI | Only value rows are editable; section headers show "section header - move to a setting to change it" |
| API | Unknown **section** → 404; unknown **key** inside a known section is created |
| Storage | `ModifyProperty` inserts or replaces the key and rewrites the whole file |
| Reload | Other clients refresh through the `SettingsChanged` event |

Type safety: every value is a string. Numeric and boolean keys are parsed by the
consumer — `session_ttl_minutes` must parse as a positive `double` in invariant
culture, `announce` must equal `true` case-insensitively; otherwise the default
applies silently.

## Restore defaults

`Settings.RestoreDefaults()`:

1. creates the app/data directories,
2. writes the embedded default JSON over `settings.json`,
3. reloads it.

Consequences:

* paths return to `APPDATA/...` defaults;
* **any custom section disappears**, including `api` — a configure-once
  `tls_pfx`/`tls_pass` is lost, and the next start falls back to plain HTTP;
* the vault is untouched (explicitly stated in both UIs' confirmations).

## Editing the file by hand

Allowed and supported while no host is running. Missing keys cause
`KeyNotFoundException` on the accessor paths (`StoreFile`, `LogFile`,
`BackupsDir`, `BackupExtension`), so keep the `local` section intact. An invalid
JSON file fails to load; delete it to regenerate defaults (paths, not data).

## Where the code lives

| Concern | File |
| ------- | ---- |
| Model, placeholders, defaults | `Doopass.Core/Configuration/Settings.cs`, `DefaultConfig.json` |
| Web page | `Doopass.WebUi/Components/Pages/Settings.razor` |
| Web session | `Cli/Services/DoopassSession.cs` |
| API | `VaultEndpoints` (`/api/settings*`), `ApiVaultCoordinator` |
| TUI | `Tui/Screens/SettingsScreen.cs`, `ValueFormScreen.cs` |
| Phone | `RemoteVaultSession.GetSettingsAsync` / `SetSettingAsync` |

Covered by `SettingsTests`: first-run creation, default paths, `APPDATA`
placeholders on disk, dropping `remote`, persistence, restore defaults, no BOM,
tolerance of a BOM, and `VaultSettingsTests` for the API round trip.
