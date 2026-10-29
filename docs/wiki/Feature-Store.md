# Feature: Store (secrets)

The core feature: managing `key = value` pairs in the unlocked vault.

## What the user can do

| Action | Web UI | TUI | REST API |
| ------ | ------ | --- | -------- |
| List keys | `/store` cards | store screen list | `GET /api/vault/keys` |
| Search | search box (case-insensitive substring) | `search` field | — (client-side) |
| Add | "Add pair" modal | `a` | `POST /api/vault/pairs` |
| Edit / rename | edit icon → modal | `e` | `PUT /api/vault/pairs` |
| Delete | delete icon → confirm | `d` → confirm | `DELETE /api/vault/pairs?key=` |
| Reveal value | eye icon (lazy fetch) | `v` | `GET /api/vault/value?key=` |
| Copy value | copy icon | `Enter` | — (client reads the value) |

## Behaviour

### Listing

* Keys are stored in insertion order and displayed **alphabetically**
  (`StringComparer.Ordinal`) in both UIs.
* The web UI shows `"N items"` or `"S of N shown"` while filtering; the TUI shows
  a matching count in its status line.
* Empty vault: the web UI shows an empty state with an "Add pair" button; the TUI
  shows "no items yet - press a to add a pair".

### Search

* Case-insensitive substring match on the key only; values are never searched.
* Web: input is debounced by 250 ms before re-rendering, because each keystroke
  otherwise round-trips over SignalR and makes mobile keyboards lag.
* TUI: filter is applied on `TextChanging` (per keystroke, local process).
* Search is purely presentational — no query reaches the storage layer.

### Reveal

* Values are never sent to the UI with the key list. Reveal performs a separate
  read (`GetValueAsync`) and caches the value only while it stays revealed.
* The web UI renders masked dots (`••••••••••`, fixed length) when hidden, so the
  value length is not leaked; the TUI renders one bullet per character up to 40
  and always shows the exact length.
* Hiding a value removes it from the cache, as does editing or deleting the key.

### Copy

* Web: `ISecretClipboard` → `doopass.copyText` → `navigator.clipboard`, with a
  hidden-textarea `execCommand('copy')` fallback. A refusal is not an error: the
  value is revealed and a toast says "Clipboard is blocked — value shown, copy it
  manually". On Android, the native `ClipboardManager` is used instead (the
  WebView API needs a live user gesture that is gone after the awaited read).
* TUI: writes through the terminal clipboard (`app.Clipboard`), and the status
  line reports `copied '<key>' (N chars) to clipboard`.
* The copied secret stays in the OS clipboard until something replaces it; the
  app cannot clear it.

### Add

| Layer | Rule |
| ----- | ---- |
| Web | Key trimmed; empty key → inline "Key is required."; duplicate → the server's `PropertyAlreadyExistError` message inline |
| TUI | `PairFormScreen`; duplicate → modal notice, form stays open |
| API | Missing/empty key → 400; duplicate → 409 |

Values may be empty. The form is closed only on success.

### Edit / rename

* Both UIs allow changing the key and the value in one step.
* The operation is `RemoveProperty(old)` + `AddProperty(new, value)`:
  * TUI checks the target key first, so a rename onto an existing key never
    destroys the old entry;
  * the web session and the API do not pre-check — a conflict leaves the old key
    removed (documented in [Known Limitations](Known-Limitations.md#rename-can-lose-the-old-key)).
* Renaming to the same key works (remove then re-add) and keeps the position at
  the end of the list.

### Delete

* Both UIs require confirmation (modal / yes-no screen) naming the key.
* Deletion is immediate and permanent; backups are the only recovery path.

## Implementation notes

| Concern | Where |
| ------- | ----- |
| Data model and persistence | `Doopass.Core.Storage.Store` |
| Web page | `Doopass.WebUi/Components/Pages/Store.razor` |
| Web copy | `Services/JsClipboard.cs`, `wwwroot/app.js` |
| TUI screen | `Doopass.Tui/Screens/StoreScreen.cs`, `PairFormScreen.cs` |
| API | `VaultEndpoints` (`/api/vault/*`), `ApiVaultCoordinator` |
| Phone transport | `RemoteVaultSession.GetKeys/GetValue/Add/Update/Remove` |
| Android copy | `Doopass.Mobile/Services/AndroidClipboard.cs` |

## Edge cases

| Case | Result |
| ---- | ------ |
| Key contains `/`, `=`, spaces or unicode | Supported; API transports it as query/JSON, not a path segment |
| Value contains `=` | Preserved (first `=` splits the line) |
| Value has leading/trailing spaces | Lost on the next load (storage trims) |
| Duplicate key added through two clients | First writer wins; the second gets a conflict |
| Two clients edit the same key | Last write wins; the other client refreshes from the pushed event |
| Revealing a key deleted elsewhere | `PropertyDoesNotExistError` → toast; the stale reveal entry is dropped |
| Very long value | No limit; it is copied to the clipboard and to memory in full |
