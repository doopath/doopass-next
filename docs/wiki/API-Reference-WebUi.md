# API Reference — Doopass.WebUi

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `Doopass.WebUi` · Source: `src/Doopass.WebUi/bin/Debug/net10.0/Doopass.WebUi.xml`

## Types

* [`Doopass.WebUi.Abstractions.HostUnreachableException`](#hostunreachableexception)
* [`Doopass.WebUi.Abstractions.IHostConnection`](#ihostconnection)
* [`Doopass.WebUi.Abstractions.ISecretClipboard`](#isecretclipboard)
* [`Doopass.WebUi.Abstractions.IVaultEvents`](#ivaultevents)
* [`Doopass.WebUi.Abstractions.IVaultSession`](#ivaultsession)
* [`Doopass.WebUi.Abstractions.VaultEventRelay`](#vaulteventrelay)
* [`Doopass.WebUi.Abstractions.VaultState`](#vaultstate)
* [`Doopass.WebUi.Services.JsClipboard`](#jsclipboard)
* [`Doopass.WebUi.Services.ToastKind`](#toastkind)
* [`Doopass.WebUi.Services.ToastService`](#toastservice)
* [`Doopass.WebUi.Services.ToastService.Toast`](#toast)

## `Doopass.WebUi.Abstractions`

### `HostUnreachableException`

Thrown by thin-client sessions when a request never reached the host:
connection refused, DNS failure, or a timeout on a stale address. Pages use
it to offer the host picker instead of reporting a bad password.

* `HostUnreachableException(string message, Exception innerException)` — Creates the exception for a host that did not answer.
  * `message` — Diagnostic message.
  * `innerException` — The timeout or socket failure that caused it.

### `IHostConnection`

Optional description of the remote host a thin client is attached to.
The web host owns its vault and never registers this service, so shared
pages check for it through `IServiceProvider` and show the host
actions only where another machine actually holds the vault.

* `Address` — Saved base URL of the host, e.g. `http://192.168.1.10:7240`.
* `IsUnreachable` — True when the last request to `Address` did not reach the host. A saved address goes stale when the host's IP changes, so the login screen needs to know before the user types a password.
* `EnsureLoadedAsync()` — Loads the saved configuration before a page reads `Address`. The login screen may render before the route guard has loaded it.
* `MarkReachable()` — Records that the host answered.
* `MarkUnreachable()` — Records that the host did not answer.
* `Changed` — Raised whenever `IsUnreachable` changes.

### `ISecretClipboard`

Platform clipboard used by the store page. The web host copies through the
browser clipboard API; the Android thin client uses the native ClipboardManager,
which does not depend on a secure context or a still-valid user gesture.
Named `ISecretClipboard` to avoid clashing with MAUI's own IClipboard.

* `SetTextAsync(string, CancellationToken)` — Puts `text` on the clipboard.
  *Returns:* `true` when the platform accepted the text.

### `IVaultEvents`

Per-scope vault event relay. Pages subscribe in `OnInitialized` and
unsubscribe in `Dispose` to refresh their state when the vault changes,
wherever the change came from: another client over the REST API (the web
host relays the server-side `IVaultChangeNotifier` into this service)
or the MAUI thin client's SignalR push connection.

* `BackupsChanged` — The backup list changed: created, restored or deleted.
* `SettingsChanged` — Application settings changed or were restored to defaults.
* `VaultChanged` — Any vault content change: pairs added, updated or removed, and store reloads.
* `VaultLocked` — The vault relocked: the caller's session ended (logout, TTL or host event).

### `IVaultSession`

The vault operations the UI components need. Both hosts implement it:
the web server wraps its per-circuit `DoopassSession` (synchronous core
storage calls wrapped in tasks), and the MAUI thin client talks to the REST
API of a remote host. Backup names are always extensionless — the host
builds the real file names itself. Methods are awaitable because the
remote implementation performs HTTP round-trips.

* `AddAsync(string key, string value, CancellationToken ct = default)` — Adds a new key/value pair.
  * `key` — Unique key; duplicates are rejected.
  * `value` — Secret value; may be empty.
  * `ct` — Cancellation token.
* `CreateBackupAsync(CancellationToken ct = default)` — Creates a dated snapshot of the current vault.
  * `ct` — Cancellation token.
* `DeleteBackupAsync(string backupName, CancellationToken ct = default)` — Deletes a snapshot.
  * `backupName` — Extensionless snapshot name.
  * `ct` — Cancellation token.
* `GetBackupNamesAsync(CancellationToken ct = default)` — Lists available snapshots.
  *Returns:* Names of all backups, newest first, without their extension.
  * `ct` — Cancellation token.
* `GetKeysAsync(CancellationToken ct = default)` — Lists every key in the vault.
  *Returns:* All keys, in storage order.
  * `ct` — Cancellation token.
* `GetSettingsAsync(CancellationToken ct = default)` — Reads every settings section with its key/value pairs.
  *Returns:* Section name plus a copy of its pairs.
  * `ct` — Cancellation token.
* `GetStateAsync(CancellationToken ct = default)` — Reads the current vault state without revealing any secret.
  *Returns:* Existence, unlock state and server version.
  * `ct` — Cancellation token.
* `GetValueAsync(string key, CancellationToken ct = default)` — Reads a single secret value.
  *Returns:* The stored value.
  * `key` — The key to read.
  * `ct` — Cancellation token.
* `InitAsync(string password, CancellationToken ct = default)` — Creates a new store and leaves it unlocked.
  *Returns:* Whether the store was created and a user-facing message.
  * `password` — The new master password (validated first).
  * `ct` — Cancellation token.
* `LoginAsync(string password, CancellationToken ct = default)` — Tries to unlock the existing store with the master password.
  *Returns:* Whether the unlock succeeded and a user-facing message.
  * `password` — The master password.
  * `ct` — Cancellation token.
* `LogoutAsync(CancellationToken ct = default)` — Locks this session: the decrypted vault and the password are dropped.
  * `ct` — Cancellation token.
* `ReloadAsync(CancellationToken ct = default)` — Re-reads the store from disk under the current password.
  * `ct` — Cancellation token.
* `RemoveAsync(string key, CancellationToken ct = default)` — Removes a key from the vault.
  * `key` — Key to remove.
  * `ct` — Cancellation token.
* `RestoreBackupAsync(string backupName, CancellationToken ct = default)` — Restores a snapshot; the host writes a safety backup first and re-reads the store.
  * `backupName` — Extensionless snapshot name.
  * `ct` — Cancellation token.
* `RestoreDefaultSettingsAsync(CancellationToken ct = default)` — Rewrites `settings.json` from the embedded defaults.
  * `ct` — Cancellation token.
* `SetSettingAsync(string section, string key, string value, CancellationToken ct = default)` — Writes a single setting value.
  * `section` — Existing section name (for example `local`).
  * `key` — Setting key inside that section.
  * `value` — New value (always a string).
  * `ct` — Cancellation token.
* `UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default)` — Renames a key and/or replaces its value (remove + add).
  * `oldKey` — Existing key.
  * `newKey` — Target key.
  * `newValue` — New value.
  * `ct` — Cancellation token.

### `VaultEventRelay`

Scoped relay implementation; a host wires its event source into it (public for cross-assembly wiring).

* `RaiseBackupsChanged()` — Raises `BackupsChanged` for every subscriber.
* `RaiseSettingsChanged()` — Raises `SettingsChanged` for every subscriber.
* `RaiseVaultChanged()` — Raises `VaultChanged` for every subscriber.
* `RaiseVaultLocked()` — Raises `VaultLocked` without awaiting the handlers.

### `VaultState`

What the client knows about the host vault: whether the store file exists,
whether some session has unlocked it, and the host version. Used by the
login / initialization screens and by thin clients probing a host.

* `IsOffline` — True only on thin clients that could not reach their host; the other values then say nothing about the vault. Always false on the web host.
* `VaultState(bool, bool, string)` — What the client knows about the host vault: whether the store file exists, whether some session has unlocked it, and the host version. Used by the login / initialization screens and by thin clients probing a host.

## `Doopass.WebUi.Services`

### `JsClipboard`

Browser clipboard. app.js tries navigator.clipboard first and falls back to
a hidden textarea plus execCommand('copy'); it returns false instead of
throwing when the browser refuses (plain HTTP on the LAN, no user gesture).

* `JsClipboard(IJSRuntime)` — Browser clipboard. app.js tries navigator.clipboard first and falls back to a hidden textarea plus execCommand('copy'); it returns false instead of throwing when the browser refuses (plain HTTP on the LAN, no user gesture).
* `SetTextAsync(string text, CancellationToken ct = default)` — Copies `text` through the browser helper; never throws, because a refused copy must not tear down the circuit.
  *Returns:* `true` when the browser accepted the text.
  * `text` — Secret to place on the clipboard.
  * `ct` — Cancellation token.

### `ToastKind`

Visual category of a toast message.

* `Error` — The operation failed.
* `Info` — Neutral information or a hint.
* `Success` — The operation succeeded.

### `ToastService`

In-memory toast queue (scoped to the current circuit). The host component renders
toasts from `Toasts` and subscribes to `Changed`.

* `Toasts` — Snapshot of the toasts currently on screen.
* `Dismiss(Guid id)` — Removes a toast immediately (also called by the auto-dismiss timer).
  * `id` — Identifier of the toast to remove.
* `Error(string title, string message)` — Shows an error toast.
  * `title` — Short headline.
  * `message` — Optional detail line.
* `Info(string title, string message)` — Shows an informational toast.
  * `title` — Short headline.
  * `message` — Optional detail line.
* `Success(string title, string message)` — Shows a success toast.
  * `title` — Short headline.
  * `message` — Optional detail line.
* `Changed` — Raised after any toast is added or removed; UI components re-render on it.

## `Doopass.WebUi.Services.ToastService`

### `Toast`

One toast message.

* `Id` — Unique identifier, used for dismissal.
* `Kind` — Visual category.
* `Message` — Optional detail line.
* `Title` — Short headline.
* `Toast(Guid Id, ToastKind Kind, string Title, string Message)` — One toast message.
  * `Id` — Unique identifier, used for dismissal.
  * `Kind` — Visual category.
  * `Title` — Short headline.
  * `Message` — Optional detail line.
