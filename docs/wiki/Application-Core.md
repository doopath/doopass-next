# Application: Core (`Doopass.Core`)

The storage and cryptography layer. It has **no project references** and uses
only the BCL. Every other project depends on it.

```
Doopass.Core
├── AppPaths.cs                 on-disk locations, DOOPASS_HOME override
├── Constants.cs                version, default iterations, backup extension, ASCII logo
├── Exceptions.cs               typed domain errors
├── TextFile.cs                 UTF-8 without BOM (shared write encoding)
├── Configuration/
│   ├── DefaultConfig.json      embedded defaults for settings.json
│   └── Settings.cs             load/save/edit settings, APPDATA substitution
├── Crypto/
│   ├── Base64Url.cs            Python-compatible url-safe base64
│   ├── Fernet.cs               Fernet token encrypt/decrypt
│   └── Pbkdf2.cs               PBKDF2-HMAC-SHA256 key derivation
├── Diagnostics/AppLogger.cs    append-only rotating file log
├── Storage/
│   ├── Store.cs                encrypted key/value vault
│   └── StoreBackup.cs          dated snapshots
└── Validation/PasswordValidator.cs
```

## `Store` — the vault

```csharp
var store = new Store(password, storePath);   // creates the file when missing
store.AddProperty("github", "hunter2");       // persists immediately
store.SetValue("github", "s3cret");
store.RemoveProperty("github");
store.GetKeys();                              // insertion order
store.GetValue("github");                     // throws PropertyDoesNotExistError
store.DoesPropertyExist("github");
```

| Member | Behaviour |
| ------ | --------- |
| `DefaultIterations` | `100_000` — used when writing |
| `Password`, `StorePath` | read-only properties |
| `GetKeys()` | copy of keys in insertion order |
| `AddProperty` | throws `PropertyAlreadyExistError` on duplicate; saves |
| `SetValue` | throws `PropertyDoesNotExistError` when missing; saves |
| `RemoveProperty` | throws `PropertyDoesNotExistError` when missing; saves |
| `Encrypt(byte[])` / `Encrypt(password, bytes, iterations)` | produces the full container string |
| `Decrypt(password, string)` | returns plaintext bytes; throws `IncorrectPasswordError` |
| `TryInitializeStore(password, path)` | `null` on wrong password (logs at DEBUG) |
| `TryInitializeExistingStore(password, path)` | throws `StoreIsNotInitializedError` if the file is absent |
| `DoesStoreFileExist(path)` | static file check |

Internals worth knowing:

* the entry list is a `List<Entry>` (`Entry` is a private record) — no
  dictionary, so insertion order and duplicate detection are explicit;
* `Load` reads the file, decrypts, splits on `\n`, trims, splits on the first
  `=`; a line without `=` becomes a key with an empty value;
* `Save` joins `"{key} = {value}"` with `\n`, encrypts with a fresh salt/IV and
  overwrites the file;
* there is no locking inside `Store`; callers serialise access (sessions hold a
  private `_gate`).

## `StoreBackup` — snapshots

```csharp
var backups = new StoreBackup(settings.StoreFile, settings.BackupsDir, settings.BackupExtension);
backups.GetBackupNames();          // extensionless, ordinal descending
backups.MakeBackup();              // dd_MM_yyyy_<store>_<n>.enc.bak
backups.SwapStoreWithBackup("26_10_2026_store_1.enc.bak"); // safety backup, then overwrite
backups.DeleteBackup("26_10_2026_store_2.enc.bak");
backups.GetStoreContent();         // raw encrypted text
backups.GetBackupContent(name);    // raw encrypted text
```

The constructor creates the backup directory. Naming and ordering rules are
specified in [Data Formats §3](Data-Formats.md#3-backups); the behavioural rules
are in [Business Rules §3](Business-Rules.md#3-backups).

## `Settings`

```csharp
var settings = new Settings();          // creates dirs + defaults on first run
settings.StoreFile;                     // resolved path
settings.BackupsDir;
settings.BackupExtension;
settings.LogFile;
settings.Sections;                      // e.g. ["local", "api"]
settings.GetSection("local");           // dictionary copy
settings.GetValue("local", "store_file");
settings.ModifyProperty("api", "session_ttl_minutes", "15");
settings.RestoreDefaults();
```

* Defaults come from the embedded `Configuration/DefaultConfig.json`; edit that
  file to change what "restore defaults" produces.
* Paths are stored with an `APPDATA/` prefix and expanded on load.
* `remote` (a legacy Python section) is dropped on load and persisted without
  it.
* `RestoreDefaults()` rewrites the file, then reloads — custom sections such as
  `api` disappear.

## `AppPaths`

| Member | Value |
| ------ | ----- |
| `Home` | platform user profile, falling back to `HOME`, then `.` |
| `AppDir` | `OverrideRoot` → `DOOPASS_HOME` → `~/doopass` |
| `AppDataDir` | `<AppDir>/appdata` |
| `SettingsFile` | `<AppDataDir>/settings.json` |
| `OverrideRoot(path)` / `ResetOverride()` | process-wide override used by tests |

## `AppLogger`

Static, thread-safe (`lock` on a private gate), fire-and-forget:

```csharp
AppLogger.Initialize(settings.LogFile);
AppLogger.Info("Store opened");
AppLogger.Debug("...");
AppLogger.Error("...", exception);
```

* Level prefix `INFO` / `DEBUG` / `ERROR`, local timestamp with milliseconds.
* Rotates at 5 MB into `<file>.1`.
* Swallows every exception: logging never breaks a request.
* Callers must pass non-secret text. Key names are acceptable; values are not.

## Crypto helpers

| Type | Purpose | Compatibility note |
| ---- | ------- | ------------------ |
| `Pbkdf2.DeriveKey` | 32-byte key, HMAC-SHA256 | matches Python `PBKDF2HMAC` |
| `Fernet.Encrypt/Decrypt` | token construction/verification | matches `cryptography.fernet.Fernet` |
| `Base64Url.Encode/Decode` | URL-safe base64 | accepts unpadded input, emits padding |

`Fernet.Decrypt` rejects tokens shorter than 57 bytes or with a version byte
other than `0x80` before touching the MAC; it never returns partial plaintext.

## Validation

`PasswordValidator` implements the inherited rules only: non-empty, no leading
space, no trailing space. `Validate()` returns `(bool Valid, string Message)`
with the user-facing message ("Password is too small!",
"Password should not start with space!", "Password should not end with space!",
"Everything is fine!"). Creation paths (`Init`) run it; unlock does not.

## Errors

| Exception | Raised when |
| --------- | ----------- |
| `IncorrectPasswordError` | MAC/padding/base64 failure, or wrong password |
| `PropertyDoesNotExistError` | reading/removing/renaming a missing key, missing backup/section |
| `PropertyAlreadyExistError` | adding a duplicate key |
| `StoreIsNotInitializedError` | opening a store that must already exist |

These types cross the REST boundary through `VaultEndpoints.MapStoreError` and
are reconstructed by `RemoteVaultSession`, so shared UI code can catch the same
types on the server and on the phone.

## Threading

* `Store` is **not** thread-safe. Each owner wraps it in a private lock:
  `DoopassSession._gate`, `ApiVaultCoordinator._gate`, TUI single-threaded loop.
* `Settings` is not thread-safe either; the coordinator serialises settings
  writes under the same gate.
* Static state: `AppPaths._overrideRoot`, `AppLogger._file`. Both are set once at
  startup (tests reset them).
