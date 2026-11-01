# Feature: Sessions and Locking

Doopass has no accounts. A "session" is a process or a bearer token that
currently holds the decrypted vault in memory. Locking means dropping that copy
and forgetting the master password.

## Session types

| Session | Scope | Created by | Destroyed by |
| ------- | ----- | ---------- | ------------ |
| TUI | the process | login / init | `G`, Esc-to-menu then quit, process exit |
| Browser UI | one token per tab, stored in `sessionStorage` | `/api/auth/login` or `/api/auth/init` | "Log out", `vaultLocked`, 401, TTL expiry, host restart |
| REST API | one token per client | `/api/auth/login` or `/api/auth/init` | logout, TTL expiry, host restart |
| Android | one token (stored in SecureStorage) | login through the phone | logout, TTL expiry, `vaultLocked` |

## Unlocking

| Path | Rule |
| ---- | ---- |
| Login | Store must exist; password verified by decrypting the file; failure = "Incorrect password!" |
| Init | Store must not exist; password validated (`PasswordValidator`); the new store is created empty and immediately unlocked |
| API login while already unlocked | The correct password adds a token; the existing shared store instance is kept (its password is not re-derived for other tokens) |

## Locking

| Trigger | TUI | Browser UI | API |
| ------- | --- | ---------- | --- |
| Explicit action | `G` from any screen | "Log out" in the sidebar | `POST /api/auth/logout` |
| Idle timeout | none | sliding TTL + absolute cap | sliding TTL + absolute cap |
| Server restart | — | unlocks lost with the process | all tokens gone |
| Other client's action | — | `vaultLocked` event → `/` | `vaultLocked` on the hub |

Locking does not touch the file, the backups or the settings — the vault stays
on disk encrypted.

## Sliding and absolute TTL

* `SessionTtl` = `api.session_ttl_minutes` minutes, else 30.
* `AbsoluteSessionTtl` = `api.absolute_session_ttl_hours` hours, else 8; an
  active token cannot outlive it.
* Every authorized REST call refreshes the token's `LastSeenUtc`
  (`TouchToken`).
* `SessionJanitor` prunes expired tokens every 30 seconds even when no request
  arrives; the same prune also runs before every token operation. An expired
  token is told on its own group, so other clients keep working.
* When the session set becomes empty, the coordinator drops the shared store and
  broadcasts `vaultLocked` to **all** hub clients.
* A hub connection does **not** refresh the TTL: only REST activity keeps a
  session alive. A phone or browser that only listens expires as designed.

Practical effect: a client left idle past the TTL gets `vaultLocked`, clears its
token and returns to the login screen; the store leaves host memory within 30
seconds of the last token expiring.

## What "the vault is unlocked" means per client

| Client | Meaning of `unlocked` |
| ------ | --------------------- |
| Browser UI | the coordinator is unlocked **and** this tab has a token |
| API `/api/vault/state` | the coordinator holds a shared store (any token can read) |
| Android | the coordinator is unlocked **and** this device has a token |

Both thin clients combine both conditions (`state.Unlocked && token != null`),
otherwise a tab would open a store page that answers 401 because another client
had unlocked the vault.

## Password lifetime

* The password is held as a `string` inside the `Store` instance; references are
  dropped on lock, so the object becomes collectable.
* .NET strings are immutable and not zeroed, so a memory dump of an unlocked
  process can reveal the password. See [Security Model](Security-Model.md#6-memory-and-lifetime-rules).
* The phone never stores the password — only the token it received.

## Multi-client behaviour

* Every API client (browser tab and phone) shares one store, so concurrent
  clients see each other's writes immediately.
* The TUI keeps its own in-process store; it converges by re-reading on change
  events, and cross-process writes are serialised by `StoreFileLock`.
* There is no ownership or "kicking" of other sessions except TTL expiry; any
  authenticated client can modify the vault.

## Failure modes

| Situation | User-visible result |
| --------- | ------------------- |
| Wrong password | "Incorrect password!" — indistinguishable from a corrupted file |
| Store missing on login | "Store is not initialized. Please initialize it first." (API 409) |
| Store exists on init | "Store already exists!" (API 409) |
| Expired token | 401 "Session expired or unauthorized."; phone clears the token |
| Host restarted while the phone is open | phone reports offline/unlock required on the next call |
| Vault locked elsewhere while a page is open | page navigates to `/` via `vaultLocked` |

## Where the code lives

| Concern | File |
| ------- | ---- |
| Browser session | `WebClient/WebApiVaultSession.cs` |
| API tokens, TTL, relock | `Cli/Api/ApiVaultCoordinator.cs` |
| Periodic expiry sweep | `Cli/Api/SessionJanitor.cs` |
| Bearer filter + limiter | `Cli/Api/VaultAuth.cs`, `Cli/Services/AuthThrottle.cs` |
| Change events | `Cli/Api/VaultChangeNotifier.cs`, `WebUi/Abstractions/VaultEvents.cs` |
| Lock routing (browser) | `WebClient/WebVaultPushClient.cs`, `WebUi/Components/Layout/MainLayout.razor` |
| Lock routing (phone) | `Mobile/Components/ConnectGuard.razor`, `Services/VaultPushClient.cs` |
| TUI lock | `DoopassWindow.RouteKey` (`G`) → `DoopassContext.Logout()` |

Tested by `VaultAuthTests`, `VaultTokenTests`, `VaultTtlTests`,
`AbsoluteSessionTtlTests`, `SessionJanitorTests`, `AuthThrottleTests` and
`RateLimitTests`.
