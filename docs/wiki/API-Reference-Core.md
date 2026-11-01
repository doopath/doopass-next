# API Reference — Doopass.Core

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `Doopass.Core` · Source: `src/Doopass.Core/bin/Release/net10.0/Doopass.Core.xml`

## Types

* [`Doopass.Core.AppPaths`](#apppaths)
* [`Doopass.Core.Configuration.Settings`](#settings)
* [`Doopass.Core.Constants`](#constants)
* [`Doopass.Core.Crypto.Base64Url`](#base64url)
* [`Doopass.Core.Crypto.Fernet`](#fernet)
* [`Doopass.Core.Crypto.Pbkdf2`](#pbkdf2)
* [`Doopass.Core.Diagnostics.AppLogger`](#applogger)
* [`Doopass.Core.IncorrectPasswordError`](#incorrectpassworderror)
* [`Doopass.Core.InvalidSettingValueError`](#invalidsettingvalueerror)
* [`Doopass.Core.PropertyAlreadyExistError`](#propertyalreadyexisterror)
* [`Doopass.Core.PropertyDoesNotExistError`](#propertydoesnotexisterror)
* [`Doopass.Core.ReadOnlySettingError`](#readonlysettingerror)
* [`Doopass.Core.Storage.AtomicFile`](#atomicfile)
* [`Doopass.Core.Storage.Store`](#store)
* [`Doopass.Core.Storage.StoreBackup`](#storebackup)
* [`Doopass.Core.Storage.StoreFileLock`](#storefilelock)
* [`Doopass.Core.StoreBusyError`](#storebusyerror)
* [`Doopass.Core.StoreCorruptedError`](#storecorruptederror)
* [`Doopass.Core.StoreIsNotInitializedError`](#storeisnotinitializederror)
* [`Doopass.Core.TextFile`](#textfile)
* [`Doopass.Core.Validation.PasswordValidator`](#passwordvalidator)

## `Doopass.Core`

### `AppPaths`

Provides the on-disk locations used by Doopass. The default root is
`~/doopass` (matching the original Python implementation) so that
existing `store.enc` and `settings.json` files are picked up
without migration. The root can be overridden (for tests / the web host)
via `OverrideRoot(string)`.

* `AppDataDir` — The application data directory.
* `AppDir` — The application root directory (`~/doopass` by default).
* `Home` — The user's home directory.
* `SettingsFile` — The settings file path.
* `OverrideRoot(string)` — Overrides the application root directory (mainly for tests).
* `ResetOverride()` — Clears any root override.

### `Constants`

Application-wide constants.

* `DefaultIterations` — Default PBKDF2 iteration count used when encrypting the store.
* `Logo` — The ASCII logo rendered on the main menu.
* `StoreBackupExtension` — Suffix appended to store backup files.
* `Version` — Application version.

### `IncorrectPasswordError`

Raised when the store cannot be decrypted with the supplied password.

* `IncorrectPasswordError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.
* `IncorrectPasswordError(string message, Exception innerException)` — Creates the error and keeps the underlying cryptographic failure.
  * `message` — Message shown to the caller.
  * `innerException` — The original exception (format or padding error).

### `InvalidSettingValueError`

Raised when a setting value fails its format validation.

* `InvalidSettingValueError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `PropertyAlreadyExistError`

Raised when trying to add a property that already exists.

* `PropertyAlreadyExistError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `PropertyDoesNotExistError`

Raised when a requested property (key) does not exist in the store.

* `PropertyDoesNotExistError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `ReadOnlySettingError`

Raised when a setting may not be changed through the UI/API (paths that decide
where data is written are read-only by policy).

* `ReadOnlySettingError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `StoreBusyError`

Raised when the store file is locked by another writer for longer than the lock timeout.

* `StoreBusyError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `StoreCorruptedError`

Raised when the store file is not a valid Doopass payload: not base64, too
short to hold a token, or declaring an absurd key-derivation cost.

* `StoreCorruptedError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `StoreIsNotInitializedError`

Raised when the store file has not been initialized yet.

* `StoreIsNotInitializedError(string message)` — Creates the error with a user-facing message.
  * `message` — Message shown to the caller.

### `TextFile`

Shared text encoding settings. The original Python application reads/writes files
as UTF-8 **without** a BOM, so every file we write must match (a BOM breaks
`json.load` and pollutes `store.enc`).

* `Utf8NoBom` — UTF-8 encoding that does not emit a byte order mark.

## `Doopass.Core.Configuration`

### `Settings`

Loads, saves and edits `settings.json`. The on-disk format mirrors the
original Doopass, using an `APPDATA/...` placeholder for paths so that
settings remain portable across machines. Defaults are embedded in the
assembly (no network access required).

* `AppDataDir` — The application data directory that holds the store, settings and logs.
* `AppDir` — The application root directory (for example `~/doopass`).
* `BackupExtension` — The backup file extension.
* `BackupsDir` — The resolved directory used for store backups.
* `LogFile` — The resolved path to the log file.
* `Sections` — The names of all top-level settings sections (currently only `local`).
* `SettingsFile` — Full path of the loaded `settings.json` file.
* `StoreFile` — The resolved path to the encrypted store file.
* `EditableKeys` — Settings the UI/API may change. Everything that names a path is read-only: a writable `store_file` or `backup_extension` turns the settings API into an arbitrary-file write (or delete) primitive.
* `ObsoleteSections` — Sections that older versions wrote to `settings.json` but that are no longer part of the configuration. They are dropped on load so the settings UI never shows them again.
* `Settings` — Loads `settings.json` for the current `AppDataDir`, creating the directories and the default file on first run.
* `GetSection(string)` — Returns all key/value pairs in a section (with substituted paths).
* `GetValue(string, string)` — Returns a substituted value from a section.
* `IsEditable(string, string)` — True when the UI/API may change this setting; paths are read-only.
* `ModifyProperty(string, string, string)` — Changes a single setting and persists it.
* `RestoreDefaults()` — Restores the embedded default configuration.

## `Doopass.Core.Crypto`

### `Base64Url`

URL-safe base64 encoding/decoding, mirroring Python's
`base64.urlsafe_b64encode` / `urlsafe_b64decode`.

* `Decode(string)` — Decodes a (possibly unpadded) URL-safe base64 string.
* `Encode(Byte[])` — Encodes bytes to a padded URL-safe base64 string.

### `Fernet`

A byte-compatible implementation of the Fernet token format
(https://github.com/fernet/spec/blob/master/Spec.md), matching the
`cryptography.fernet.Fernet` behavior used by the original Doopass.

A token is: version (0x80) || timestamp (8 bytes, big-endian) ||
IV (16 bytes) || AES-128-CBC ciphertext (PKCS7) || HMAC-SHA256 (32 bytes),
all encoded as URL-safe base64.

* `Decrypt(Byte[], string)` — Decrypts a `token` with a 32-byte raw key. Throws `IncorrectPasswordError` when the token cannot be authenticated (wrong key or corrupted data).
* `Encrypt(Byte[], Byte[])` — Encrypts `plaintext` with a 32-byte raw key.

### `Pbkdf2`

PBKDF2-HMAC-SHA256 key derivation (same parameters as the original).

* `DeriveKey(Byte[], Byte[], int)` — Derives a 32-byte key from a password and salt.
* `DeriveKey(string, Byte[], int)` — Derives a 32-byte key from a password string and salt.

## `Doopass.Core.Diagnostics`

### `AppLogger`

Minimal file logger shared by the TUI and the web host. Writes append-only,
timestamped lines to the configured log file and rotates it at 5 MB.
Logging must never throw into application code.

* `Debug(string message)` — Writes a debug line (used for non-fatal diagnostics).
  * `message` — Message to append; must not contain secrets.
* `Error(string message)` — Writes an error line.
  * `message` — Message to append; must not contain secrets.
* `Error(string message, Exception exception)` — Writes an error line including the exception's full text.
  * `message` — Context message.
  * `exception` — Exception whose `ToString()` is appended.
* `Info(string message)` — Writes an informational line.
  * `message` — Message to append; must not contain secrets.
* `Initialize(string)` — Enables logging to the given file.

## `Doopass.Core.Storage`

### `AtomicFile`

Writes a text file atomically: the content goes to a temp file in the same
directory and is then renamed over the target. A crash, a full disk or a
parallel reader can therefore never observe a half-written store.enc or
settings.json (the old content stays visible until the rename).

* `CopyUnixMode(string, string)` — Keeps the previous permissions (a fresh file would be 0644, exposing a 0600 store).
* `SweepStaleTempFiles(string, string)` — Removes temp files a crashed or killed process left behind for the same target an hour ago. Best effort: a cleanup failure must never fail the write that follows.
* `WriteAllText(string path, string contents, Encoding encoding)` — Writes `contents` to `path` via a temp file and a rename.
  * `path` — Target file path.
  * `contents` — Text to write.
  * `encoding` — Encoding used for the temp file.

### `Store`

An encrypted key/value store. The on-disk format is fully compatible with
the original Doopass: `base64url( salt[16] + iterations[4] + fernet_token )`,
where the plaintext is a list of `key = value` lines.

* `Password` — The master password for this store (kept in memory only).
* `StorePath` — The path to the encrypted store file.
* `DefaultIterations` — Default PBKDF2 iteration count written into new store containers.
* `_io` — Guards `_entries` and `_stamp` and serialises disk I/O.
* `_stamp` — "mtime ticks:length" of the file the in-memory entries came from.
* `Store(string password, string storePath)` — Opens the store at `storePath` with `password` and loads all entries. When the file does not exist, an empty encrypted store is created first.
  * `password` — The master password used to decrypt the file.
  * `storePath` — Full path of the `store.enc` file.
  *Throws* `IncorrectPasswordError`: The file cannot be decrypted with the password.
  *Throws* `StoreCorruptedError`: The file is not a valid vault container.
* `AddProperty(string, string)` — Adds a new key/value pair to the store.
* `Decrypt(string, string)` — Decrypts a full store payload (salt + iterations + token).
* `DoesPropertyExist(string)` — Returns true when a property with the given name exists.
* `DoesStoreFileExist(string)` — Returns true when the store file exists on disk.
* `Encrypt(Byte[])` — Encrypts plaintext bytes with the store password.
* `Encrypt(string, Byte[], int)` — Encrypts plaintext bytes with an explicit password.
* `EnsureFresh_locked()` — Reloads from disk when the file changed since the last read/write.
* `GetKeys()` — Returns all keys in insertion order.
* `GetValue(string)` — Returns the value for the given key.
* `Mutate_locked(Entry}})` — Runs a mutation against the on-disk state, not against whatever this instance happens to have cached: two Store instances (web sessions, TUI, another process) must not overwrite each other's entries.
* `RemoveProperty(string)` — Removes an existing key.
* `SetValue(string, string)` — Updates the value of an existing key.
* `StampOf(string)` — "<mtime ticks>:<length>": cheap enough to check on every read.
* `TryInitializeExistingStore(string, string)` — Initializes a store that must already exist on disk. Throws `StoreIsNotInitializedError` when it does not.
* `TryInitializeStore(string, string)` — Tries to initialize a store; returns `null` if the password is wrong.

### `StoreBackup`

Creates, lists, restores and deletes dated backups of the encrypted store,
replicating the original Doopass backup naming scheme.

* `StoreBackup(string storePath, string backupsDir, string extension)` — Creates a backup helper for `storePath` and makes sure the backup directory exists.
  * `storePath` — The encrypted store file to snapshot.
  * `backupsDir` — Directory that holds the snapshots.
  * `extension` — File-name suffix for snapshots (default `.enc.bak`).
* `DeleteBackup(string)` — Deletes a backup by name (including extension).
* `GetBackupContent(string)` — Reads the raw content of a backup by name (including extension).
* `GetBackupNames()` — Returns the names (without extension) of all backups, newest first.
* `GetStoreContent()` — Returns the raw (still encrypted) content of the store file.
* `IsSafeBackupName(string)` — True for a plain, extensionless backup name (letters, digits, '_', '-', '.'). Rejects anything that could escape the backups directory.
* `MakeBackup()` — Creates a new dated backup of the current store.
* `ResolvePath(string)` — Resolves a backup file name inside the backups directory and rejects any name whose resolved path would leave it (defence in depth: TUI, web-UI and API all end up here). The error deliberately matches "no such backup" so a rejected path is indistinguishable from a missing one.
* `SwapStoreWithBackup(string)` — Replaces the current store with a backup (making a backup first). Holds the same cross-process lock as `Store` mutations, so a pair added by another client cannot land between the read and the swap and be lost.

### `StoreFileLock`

A cross-process exclusive lock on the store file, implemented as a sidecar
`store.enc.lock` opened with `None`. The lock file
is intentionally never deleted: on Windows an open file cannot be removed,
and on Unix deleting it races with another process that just opened it. An
empty sidecar next to `store.enc` is harmless.

* `Acquire(string, TimeSpan})` — Acquires the lock, waiting up to `timeout` (5 s by default).
* `Dispose()` — Releases the cross-process lock.

## `Doopass.Core.Validation`

### `PasswordValidator`

Validates the master password using the original Doopass rules.

* `Password` — The password being validated.
* `PasswordValidator(string password)` — Creates a validator for the given candidate password.
  * `password` — The password typed by the user.
* `Validate()` — Returns a tuple: whether the password is valid, and a human-readable message.
