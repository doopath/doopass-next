# Feature: Backups

Dated snapshots of the encrypted vault. They are byte copies of `store.enc`, so
they are encrypted with the same master password and can be restored without any
extra key.

## What the user can do

| Action | Web UI | TUI | REST API |
| ------ | ------ | --- | -------- |
| List | `/backups` | backups screen | `GET /api/backups` |
| Create | "Create backup" | `c` | `POST /api/backups` |
| Restore | "Restore" → confirm | `r` → confirm | `POST /api/backups/restore?name=` |
| Delete | delete icon → confirm | `d` → confirm | `DELETE /api/backups?name=` |

## Naming

```
dd_MM_yyyy_<storeBase>_<n>.enc.bak
```

* `dd_MM_yyyy` — local date (`DateTime.Today`).
* `<storeBase>` — the store file name without `.enc` (`store` by default).
* `<n>` — next number for that day, starting at 1.
* extension from `settings.local.backup_extension` (default `.enc.bak`).

Example session on 26 October 2026:

```
26_10_2026_store_1.enc.bak
26_10_2026_store_2.enc.bak
27_10_2026_store_1.enc.bak
```

## Create

`StoreBackup.MakeBackup()`:

1. computes today's base name;
2. finds existing backups with that prefix, takes the last in ordinal order and
   increments its trailing number (or starts at 1);
3. copies the current store text into the new file (UTF-8, no BOM);
4. logs `Backup created: <file name>`.

There is no encryption/decryption in this path: the file is already ciphertext.

## Restore

`StoreBackup.SwapStoreWithBackup(name)`:

1. reads the backup content **first** (so a missing backup fails before anything
   is touched);
2. creates a safety backup of the current vault (`MakeBackup`);
3. overwrites `store.enc` with the backup content;
4. logs `Store restored from backup: <file>`.

Then the session re-reads the store from disk with the same password:

| Client | Reload path |
| ------ | ----------- |
| Web UI | `DoopassSession.RestoreBackupAsync` → `Reload()` |
| TUI | `Context.ReloadStore()` |
| REST API | `ApiVaultCoordinator.RestoreBackup` → `new Store(password, path)` |

Notifications: a restore raises **both** `BackupsChanged` and `VaultChanged`, so
open pages refresh their data and their backup list.

Because a safety backup is taken first, a restore is always undoable: the
pre-restore state appears as the newest snapshot of the day.

## Delete

`StoreBackup.DeleteBackup(name)` deletes the file if it exists. The web session
and the API check existence first and report "Backup '<name>' does not exist!";
the storage layer itself is idempotent. Deletion is not confirmed twice and is
irreversible (no trash, no archive).

## Listing and ordering

`GetBackupNames()` returns **extensionless** names, ordinal descending. The
practical effect:

* within one day, the highest number is first — i.e. newest first;
* across months, `29_10_2026_…` sorts before `01_11_2026_…` because the
  comparison is lexicographic on `dd_MM_yyyy`. The list is therefore
  "grouped by day", not strictly chronological.

The UI shows names verbatim; it does not parse or reformat dates.

## What is not included

* No automatic/periodic backups, no retention or pruning, no compression.
* No backup of `settings.json` — restoring settings is done with "Restore
  defaults" or by editing the file.
* No cloud upload, no off-site copy. Backups live next to the vault
  (`appdata/backups`) and inherit its access rules.
* No integrity check before restore: a corrupted backup is written as-is and the
  next unlock fails with "Incorrect password!". Keep more than one snapshot.

## Where the code lives

| Concern | File |
| ------- | ---- |
| Storage | `Doopass.Core/Store/StoreBackup.cs` |
| Web page | `WebUi/Pages/Backups.razor` |
| Web session | `Cli/Services/DoopassSession.cs` (`*BackupAsync`) |
| API | `Api/VaultEndpoints.cs`, `Api/ApiVaultCoordinator.cs` |
| TUI | `Tui/Screens/BackupScreen.cs` |
| Phone | `Mobile/Services/RemoteVaultSession.cs` |

Covered by: `StoreTests` (storage CRUD), `VaultDataTests.T02` (API backup
lifecycle, including the safety backup), and the TUI navigation tests.
