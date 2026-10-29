# Testing

```bash
dotnet build Doopass.slnx         # must be warning-free
dotnet test  Doopass.slnx         # 103 tests, ~7 s
```

Current baseline (2026-10): **103 passed, 0 failed** — Core 34, TUI 50, API 19.
The Android project is not built by the solution and has no automated tests.

## Projects

| Project | References | Purpose |
| ------- | ---------- | ------- |
| `tests/Doopass.Core.Tests` | `Core` | format compatibility, storage CRUD, settings, validation, logging |
| `tests/Doopass.Tui.Tests` | `Tui` | key mapping, focus ring, navigation, rendered screen content |
| `tests/Doopass.Api.Tests` | `Cli` | the real web host on a random port: auth, CRUD, backups, settings, TTL, rate limit, discovery payload |

All three use xUnit (`Microsoft.NET.Test.Sdk` 17.11.1, xunit 2.9.2).

## Core tests — what they pin down

| File | Coverage |
| ---- | -------- |
| `FernetCompatibilityTests` | a golden store produced by the Python implementation decrypts to the expected plaintext; PBKDF2 matches a Python-derived key; encrypt→decrypt round trip; wrong password throws; raw Fernet round trip |
| `StoreTests` | new store creates an encrypted file; add + reload persists; duplicate throws; update/remove; missing key throws; `=` inside a value survives; `TryInitializeStore` returns null on a bad password; `TryInitializeExistingStore` throws when missing; no UTF-8 BOM |
| `SettingsTests` | first-run file creation; default paths; `APPDATA` placeholder on disk; only `local` by default; `remote` dropped; persistence; restore defaults; path re-substitution; no BOM; BOM input tolerated |
| `PasswordValidatorTests` | empty, leading space, trailing space rejected; normal passwords accepted |
| `AppLoggerTests` | store operations are logged and neither password nor secret value appears |

The compatibility tests are the most important ones: they are the only guard
that existing users' files keep opening.

## TUI tests

| File | Coverage |
| ---- | -------- |
| `TuiKeyMapTests` | every `(key, context)` combination: Tab/Shift+Tab, Esc back vs quit, F1, editable fields owning letters, list ownership, H/L, J/K |
| `FocusRingTests` | wrapping forward/backward, unknown current index, empty ring |
| `NavigationTests` | menu movement with `j/k` and arrows, Tab traversal, Esc/back, store shortcuts, vault locking, screen transitions |
| `ScreenDumpTests` | rendered text of each screen (labels, panels, hints) without a real terminal |

They use the real `DoopassWindow` with a fake driver, so focus-routing changes are
caught without typing.

## API tests

`ApiHostFixture` starts the real host (`WebHostFactory.BuildWebHost`) on
`127.0.0.1:0` with a temporary `DOOPASS_HOME`, exposes an `HttpClient` and the
`ApiVaultCoordinator`, and resets state per test (`Coordinator.TestReset()`
deletes the store and backups and restores the clock).

| File | Coverage |
| ---- | -------- |
| `VaultAuthTests` | state is anonymous and reports a missing store; login before init → 409; init issues a token; init twice → 409; wrong password → 401; validator message → 400 |
| `VaultDataTests` | full pair CRUD; backup lifecycle (create, restore, safety backup, key changes); reload re-reads disk |
| `VaultSettingsTests` | settings round trip and restore-defaults through the API |
| `VaultTokenTests` | logout invalidates every protected endpoint |
| `VaultTtlTests` | token survives a fresh TTL window; expires past the TTL and relocks; logging out the last session relocks the coordinator |
| `RateLimitTests` | six bad logins trip 429; the rejection body is the JSON error shape |
| `DiscoveryPayloadTests` | reply fields, null fingerprint without TLS, probe constant |

`CollectionBehavior.cs` disables test parallelisation because the host binds
real ports and shares process-wide state (`AppPaths`, `AppLogger`).

## What is *not* covered

* `Doopass.Mobile` — no automated tests; verified manually on a device.
* Web UI components — no bUnit tests; behaviour is exercised through the API
  tests and manually.
* End-to-end browser automation — `docs/E2E-CHECKLIST.md` (git-ignored) is the
  manual list.
* Cryptography beyond compatibility vectors — the scheme is inherited, not
  designed here.
* Concurrency — the "two stores" race is documented, not tested.
* Performance and load.

## Adding tests

1. Put the test next to the behaviour it protects and name it after the rule
   (`ValueContainingEquals_IsPreserved`, `T03_Logout_of_the_last_session_relocks_the_coordinator`).
2. Core logic gets a unit test with a temporary `DOOPASS_HOME`
   (`AppPaths.OverrideRoot` or the env var helper).
3. API behaviour goes into `Doopass.Api.Tests`, starting from
   `Reset()` → `InitAsync`/`LoginAsync` → assertions.
4. TUI behaviour: if it is a key or focus decision, add it to `TuiKeyMapTests` /
   `NavigationTests`; if it is visual text, add a screen dump assertion.
5. Keep the suite under ~10 s; no sleeps longer than the fake-clock TTL tests
   already need.

## Manual smoke tests

```bash
./scripts/smoke-api.ps1     # REST API on a throwaway home (PowerShell)
./scripts/smoke-tls.ps1     # TLS host + fingerprint + pinned request
```

Both keep their scratch data under `scripts/.smoke-*/` (git-ignored).
