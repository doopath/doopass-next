# Business Rules

This page is the behavioural contract. Each rule names the component that
enforces it, so a change in the code can be traced to a change in the product.

## 1. Vault (store)

### 1.1 Identity and existence

| Rule | Details |
| ---- | ------- |
| The vault is a single file | Path comes from `settings.json` → `local.store_file`, default `APPDATA/store.enc`. `DOOPASS_HOME` moves the whole root. |
| "Store exists" means the file exists | `Store.DoesStoreFileExist`. An empty directory is not an empty vault. |
| Opening a vault creates it if missing | `new Store(password, path)` writes an empty encrypted file when the path does not exist. This is why "init store" and "login" both end up constructing a `Store`. |
| Init refuses to overwrite | Web, TUI and API all check existence first and return "Store already exists!". |
| No vault migration | The file layout is fixed; unknown/extra files are ignored. |

### 1.2 Keys and values

| Rule | Details |
| ---- | ------- |
| Keys are unique | `AddProperty` throws `PropertyAlreadyExistError` for an existing key. Comparison is ordinal (case-sensitive). |
| Keys are arbitrary strings | No length limit, no charset restriction. API transports keys as query parameters or JSON, never path segments, because keys may contain `/`, `=` or unicode. |
| Values are arbitrary strings | Empty values are allowed; `=` inside a value is preserved (test: `ValueContainingEquals_IsPreserved`). |
| Insertion order is preserved | `GetKeys()` returns keys in the order they were added; UIs sort alphabetically for display. |
| Whitespace is normalised on load | Each stored line is trimmed; the key is trimmed; the value is trimmed. A value with meaningful leading/trailing spaces does not survive a reload — an inherited Python-format constraint. |
| Rename = remove + add | Every UI and the API implement "update" this way. The TUI checks for a duplicate target key *before* removing, so a failed rename never loses the old entry. The API and web session do not pre-check (see [Known Limitations](Known-Limitations.md#rename-can-lose-the-old-key)). |
| Deleting a missing key fails | `RemoveProperty` throws `PropertyDoesNotExistError`. |
| Reading a missing key fails | `GetValue` throws `PropertyDoesNotExistError`; callers surface it as a message/toast, never as an empty value. |
| One value per key | There are no structured records, tags or metadata. |

### 1.3 Persistence

| Rule | Details |
| ---- | ------- |
| Every mutation is persisted immediately | `AddProperty`, `SetValue`, `RemoveProperty` rewrite the whole file before returning. |
| The write is the only durability point | No debounce, no batching, no transaction across multiple mutations (a rename performs two writes). |
| The password is never re-derived for reads | The in-memory `Store` holds the decrypted list and the master password string. |
| Reload re-decrypts from disk | Used after a backup restore and when another client changed the vault. |
| A wrong password is indistinguishable from corruption | Both fail HMAC verification and surface `IncorrectPasswordError`. |

## 2. Master password

| Rule | Details |
| ---- | ------- |
| Validation (creation only) | Non-empty; must not start with a space; must not end with a space. Source: `PasswordValidator`. |
| No strength policy | No length/complexity requirement beyond the above. |
| Confirmation is a UI rule | Web `/init` and TUI init screen require the two fields to match; the API takes one password. |
| Unlock failure is generic | "Incorrect password!" — no distinction between wrong password, truncated file or tampered token. |
| Password is kept as a .NET string | It cannot be reliably zeroed; the reference is dropped on lock/exit. Anyone with process memory access while unlocked can read it. |
| No recovery path | Backups are encrypted with the same password. |

## 3. Backups

| Rule | Details |
| ---- | ------- |
| Naming | `dd_MM_yyyy_<storeBase>_<n>.enc.bak`, e.g. `26_10_2026_store_1.enc.bak`. `n` starts at 1 per day and increments. |
| Backups are byte copies | Encrypted content only; restoring needs no password at file level, but the file is useless without it. |
| Date is local `DateTime.Today` | Two machines in different time zones can produce differently named backups for the same moment. |
| Listing | Extensionless names; **ordinal descending** sort. Newest-first holds within a month; names do not sort chronologically across month/year boundaries. |
| Restore always takes a safety backup | `SwapStoreWithBackup` calls `MakeBackup()` *before* overwriting the store, so a restore is itself undoable. |
| Restore replaces the file, then sessions reload | TUI calls `ReloadStore`, web session calls `Reload`, API coordinator re-creates its `Store`. |
| Delete is idempotent at the storage layer | Missing file is ignored by `StoreBackup.DeleteBackup`; the API and UI explicitly check first and return "Backup '<name>' does not exist!". |
| Backup directory is auto-created | `StoreBackup` constructor creates it, as does `Settings` startup. |
| No retention policy | Nothing is pruned automatically. Unlimited backups are allowed. |
| Names are untrusted input | The API appends the configured extension and checks the resolved path exists; callers pass extensionless names only. |

## 4. Settings

| Rule | Details |
| ---- | ------- |
| Format | JSON object: `{ "<section>": { "<key>": "<value>" } }`. Values are always strings. |
| Defaults | Embedded `Doopass.Core.Configuration.DefaultConfig.json`, written on first run or on "restore defaults". |
| Sections | `local` is the only default section. `api` is optional and user-created. |
| `remote` is obsolete | Dropped on load and persisted without it. |
| Path values | Stored with an `APPDATA/` placeholder; substituted to the real data directory on load and un-substituted on save. |
| Unknown sections are preserved | Only `remote` is dropped; anything else the user adds stays. |
| Editing an unknown key creates it | `ModifyProperty` inserts into the section dictionary. The REST API rejects unknown **sections** but allows new keys. |
| Missing key/section throws | `GetValue` / `GetSection` use dictionary indexers; callers must know the section exists. |
| Restore defaults rewrites the file | Any custom `[api]` section (TLS paths, TTL, announce) is lost. |
| Settings are not encrypted | Paths and optional TLS password are plain text on disk. |
| Settings are host-wide | Every client edits the same `settings.json`; there is no per-user settings file. |

## 5. Sessions, tokens and locking

### 5.1 Web UI

| Rule | Details |
| ---- | ------- |
| Unlock scope | One Blazor circuit = one `DoopassSession` = one `Store`. |
| Redirect rules | `/store`, `/backups`, `/settings` redirect to `/` when the circuit's state is not unlocked. `/` redirects to `/store` when it is. |
| Logout | Drops the store reference; navigates to `/`. No token, no cookie: the session simply forgets the password. |
| Lock event | `VaultLocked` (from the server notifier, or the phone's push client) sends the user to `/`. |
| No idle timeout | A circuit stays unlocked until logout, disconnect, or server restart. Blazor's own circuit timeout applies. |
| No login rate limiting | The limiter covers only `/api/auth/*`. |

### 5.2 REST API

| Rule | Details |
| ---- | ------- |
| Tokens | 32 random bytes, base64url, no padding; opaque, stored in memory only. |
| TTL | Sliding, default 30 minutes, configurable via `api.session_ttl_minutes` (>0, invariant culture). |
| Sliding | Any authorized request calls `TouchToken` and refreshes the timestamp. Hub connections validate without sliding. |
| Expiry | Checked before every operation; expired tokens are removed and their clients receive `vaultLocked`. |
| Relock | When the token set becomes empty (logout or expiry), the shared store is dropped and *all* hub clients receive `vaultLocked`. |
| Login is password-verified even when unlocked | A second client must still supply the correct master password; a correct one just adds a token and keeps the existing store instance. |
| Init only when no store exists | Returns 409 otherwise. |
| Logout | Removes the caller's token; idempotent (unknown token still returns 204). |
| API surface is host-wide | `unlocked` in `/api/vault/state` reports whether *the coordinator* holds an unlocked store, not whether the caller can read it. A thin client additionally requires a token of its own. |

## 6. Change notification

| Rule | Details |
| ---- | ------- |
| Every mutation raises an event | Pairs → `VaultChanged`; backups → `BackupsChanged`; settings → `SettingsChanged`; lock → `VaultLocked`. |
| Restore raises two | A backup restore raises `BackupsChanged` *and* `VaultChanged`, because vault content changed too. |
| Fan-out | `VaultChangeNotifier` is server-wide; the hub relay pushes to SignalR clients; each circuit's relay wakes open pages. |
| Hub scope | Content events go to *all* connected clients (they still need a valid token to read data); lock events target the affected token group, or everyone when no sessions remain. |
| Pages re-read, they do not receive data | Events carry no payload. Receivers call `Reload`/`GetKeys` again, so the file is the single source of truth. |
| Self-notification | A client that made the change also receives its own event and refreshes; handlers are idempotent. |

## 7. Remote access (thin clients)

| Rule | Details |
| ---- | ------- |
| The phone never stores secrets | Only host URL, certificate fingerprint and session token, in Android `SecureStorage`. |
| Host must be reachable | State probe timeout 4 s, auth timeout 8 s, general HTTP timeout 20 s. A stale address reports "offline", never "wrong password". |
| Offline keeps the token | A stale address must not log the user out; the login screen shows a banner and offers the host picker. |
| TLS pinning | When the host URL is `https` and a pin exists, the certificate's SHA-256 must match exactly (hex, case-insensitive). Host name/SAN is not checked. |
| Re-pinning requires confirmation | Picking a host again shows its current fingerprint; "Trust and connect" stores the new pin. A changed certificate without re-pairing fails the handshake. |
| Plain HTTP is allowed but warned | No pin is stored for `http`; the UI shows a permanent warning. |
| Discovery is best-effort | No reply within 1.5 s means "not found"; manual entry is the fallback. |
| Lock propagation | `vaultLocked` for the current host/token clears the stored token and routes to login. Late events from a previous host/token are ignored. |

## 8. Errors and messages

| Layer | Rule |
| ----- | ---- |
| Core | Typed exceptions: `IncorrectPasswordError`, `PropertyDoesNotExistError`, `PropertyAlreadyExistError`, `StoreIsNotInitializedError`. |
| REST | Always `{"error":"<message>"}`; 400 validation, 401 auth/session, 404 missing key/backup/section, 409 duplicate/not initialized/already exists, 429 rate limit, 500 unexpected. |
| Web UI | Form-level errors for validation, toasts for operations, `ErrorBoundary` for unexpected render failures. |
| TUI | Inline error labels and modal `notice`/`confirm` screens. |
| Logs | Full exception text; **never** passwords, secret values or tokens. |

## 9. Non-functional rules

| Rule | Details |
| ---- | ------- |
| Encoding | UTF-8 **without BOM** for every written file (a BOM breaks the Python reader). |
| Logging never throws | `AppLogger` swallows all I/O failures. |
| Log rotation | 5 MB threshold, one archive (`doopass.log.1`), no compression. |
| Asset caching | Text assets revalidate (`no-cache`); icons/blazor script cache for an hour. |
| Version | `5.0.0` in `Constants.Version`, all `.csproj` files; reported by `/api/vault/state` and the UI chrome. |
| Language | UI copy and code comments in English; commit messages follow `type(scope): summary`. |
