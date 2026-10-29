# Feature: Sessions and Locking

Doopass has no accounts. A "session" is a process, a browser circuit or an API
token that currently holds the decrypted vault in memory. Locking means dropping
that copy and forgetting the master password.

## Session types

| Session | Scope | Created by | Destroyed by |
| ------- | ----- | ---------- | ------------ |
| TUI | the process | login / init | `G`, Esc-to-menu then quit, process exit |
| Web UI | one Blazor circuit | login / init | "Log out", circuit end, `vaultLocked`, server restart |
| REST API | one token per client | `/api/auth/login` or `/api/auth/init` | logout, TTL expiry, host restart |
| Android | one token (stored) | login through the phone | logout, TTL expiry, `vaultLocked` |

## Unlocking

| Path | Rule |
| ---- | ---- |
| Login | Store must exist; password verified by decrypting the file; failure = "Incorrect password!" |
| Init | Store must not exist; password validated (`PasswordValidator`); the new store is created empty and immediately unlocked |
| API login while already unlocked | The correct password adds a token; the existing shared store instance is kept (its password is not re-derived for other tokens) |

## Locking

| Trigger | TUI | Web UI | API |
| ------- | --- | ------ | --- |
| Explicit action | `G` from any screen | "Log out" in the sidebar | `POST /api/auth/logout` |
| Idle timeout | none | none (Blazor circuit lifetime only) | sliding TTL, default 30 min |
| Server restart | — | unlocks lost with the process | all tokens gone |
| Other client's action | — | `vaultLocked` event → `/` | `vaultLocked` on the hub |

Locking does not touch the file, the backups or the settings — the vault stays
on disk encrypted.

## Sliding TTL (REST)

* `SessionTtl` = `api.session_ttl_minutes` minutes, else 30.
* Every authorized REST call refreshes the token's `LastSeenUtc`
  (`TouchToken`).
* Expired tokens are pruned before every token operation; pruned clients are
  sent `vaultLocked`.
* When the session set becomes empty, the coordinator drops the shared store and
  broadcasts `vaultLocked` to **all** hub clients.
* A hub connection does **not** refresh the TTL: only REST activity keeps a
  session alive. A phone that only listens expires as designed.

Practical effect: a phone left on the store screen for longer than the TTL gets
`vaultLocked` on its next action, clears the token and returns to the login
screen.

## What "the vault is unlocked" means per client

| Client | Meaning of `unlocked` |
| ------ | --------------------- |
| Web UI | this circuit holds a `Store` |
| API `/api/vault/state` | the coordinator holds a shared store (any token can read) |
| Android | the coordinator is unlocked **and** this device has a token |

The Android client deliberately combines both conditions
(`state.Unlocked && token != null`), otherwise it would open an empty store page
when only the PC's browser had unlocked the vault.

## Password lifetime

* The password is held as a `string` inside the `Store` instance; references are
  dropped on lock, so the object becomes collectable.
* .NET strings are immutable and not zeroed, so a memory dump of an unlocked
  process can reveal the password. See [Security Model](Security-Model.md#6-memory-and-lifetime-rules).
* The phone never stores the password — only the token it received.

## Multi-client behaviour

* API tokens share one store, so concurrent API clients see each other's writes
  immediately.
* Each browser circuit and the TUI keep their own store instance; they converge
  by reloading on change events. A write from one does not invalidate another's
  in-memory copy, but the next change event triggers a reload.
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
| Web circuit session | `Cli/Services/DoopassSession.cs` |
| API tokens, TTL, relock | `Cli/Api/ApiVaultCoordinator.cs` |
| Bearer filter + limiter | `Cli/Api/VaultAuth.cs` |
| Change events | `Cli/Api/VaultChangeNotifier.cs`, `WebUi/Abstractions/VaultEvents.cs` |
| Lock routing (web) | `WebUi/Components/Layout/MainLayout.razor` |
| Lock routing (phone) | `Mobile/Components/ConnectGuard.razor`, `Services/VaultPushClient.cs` |
| TUI lock | `DoopassWindow.RouteKey` (`G`) → `DoopassContext.Logout()` |

Tested by `VaultAuthTests`, `VaultTokenTests`, `VaultTtlTests`
(`T01` sliding window, `T02` expiry + relock, `T03` last-session relock) and
`RateLimitTests`.
