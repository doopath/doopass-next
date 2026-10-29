# Feature: Real-time Sync

Every change made by any client becomes visible to every other client without a
page reload. The mechanism is a server-side event → SignalR → receiving client's
event relay → page re-read.

## Event flow

```
 write (web page)                         write (phone / API token)
      |                                            |
 DoopassSession.Add                       ApiVaultCoordinator.Add
      \_______________________  ___________________/
                              \/
                 IVaultChangeNotifier (singleton)
                   VaultChanged / BackupsChanged
                   SettingsChanged / VaultLocked
                    /                          \
      VaultHubRelay (hosted)             DoopassSession (each circuit)
                    |                          |
          SignalR /hubs/vault            VaultEventRelay (scoped)
                    |                          |
        phone: VaultPushClient          Store.razor / Backups.razor /
                    |                   Settings.razor / MainLayout
        VaultEventRelay → pages re-read
```

## Server side

| Type | Role |
| ---- | ---- |
| `IVaultChangeNotifier` | event source; implemented by `VaultChangeNotifier` (singleton) |
| `VaultHubRelay` | `BackgroundService` subscribing to the notifier and sending to SignalR clients |
| `VaultHub` | validates `access_token` on connect, joins group `doopass:<token>` |
| `DoopassSession` | subscribes the notifier to its circuit relay in the constructor, unsubscribes in `Dispose` |

Event payloads are empty: receivers re-read state. This keeps the protocol small
and guarantees the file stays the source of truth.

Broadcast scopes:

| Event | Scope |
| ----- | ----- |
| `vaultChanged`, `backupsChanged`, `settingsChanged` | all connected hub clients |
| `vaultLocked` with a token | that token's group only |
| `vaultLocked` with an empty token (no sessions left) | all clients |

Lock events are targeted so a phone that is still validly logged in does not get
kicked when the PC's browser logs out — but when the **last** session ends, the
whole vault is relocked and everyone is told.

## Web UI

Pages subscribe in `OnInitializedAsync` / `OnInitialized` and unsubscribe in
`Dispose`:

| Page | Subscriptions | Reaction |
| ---- | ------------- | -------- |
| `Store.razor` | `VaultChanged` | `ReloadAsync`, re-read keys, refresh revealed values, drop deleted ones |
| `Backups.razor` | `BackupsChanged`, `VaultChanged` | refresh the snapshot list |
| `Settings.razor` | `SettingsChanged` | refresh sections |
| `MainLayout.razor` | `VaultLocked` | navigate to `/` |

Handlers marshal back to the renderer with `InvokeAsync`, and only call
`StateHasChanged` when the component is still on its route.

## Android client

`VaultPushClient` connects to `<host>/hubs/vault` with the token and forwards
hub messages into the shared `VaultEventRelay`, so the same Razor pages react
identically. Details:

* automatic reconnect at 0, 1, 5, 15, 30 seconds;
* `IsCurrentSession` prevents reusing a connection bound to an old host/token;
* `vaultLocked` is honoured only if the host **and** token still match the saved
  ones (late events after switching hosts are ignored);
* while the host is marked unreachable, the guard does not start a connection.

## What is *not* synchronised

* **No replay/queue.** A client that is offline misses events; it re-reads on
  reconnect (page load, next action) rather than receiving a delta.
* **No payload diffing.** Every event causes a full key list read.
* **No file watching.** Changes made by editing `store.enc` outside the app (or
  by the TUI, which has no notifier) are not pushed. The API's
  `POST /api/vault/reload` exists for that case.
* **No cross-process sync.** Two hosts on the same directory do not see each
  other's writes; only clients of the same process are notified.

## TUI

The TUI runs in the same process as the store but is a separate front-end; it
does not participate in the notifier because a single TUI owns its own store and
there is no concurrent writer in that mode (`doopass` and `doopass serve` are
alternative modes).

## Verifying it

1. `doopass serve --lan`, log in from a browser and from a phone.
2. Add a pair in the browser → the phone's store page updates within a second.
3. Restore a backup on the phone → the browser's backup list and store page both
   refresh.
4. Press "Log out" in the browser (with no other API session) → the phone shows
   the login screen.

## Where the code lives

* `Cli/Api/VaultChangeNotifier.cs` — event contract
* `Cli/Api/VaultHub.cs` — hub + relay
* `Cli/Services/DoopassSession.cs` — circuit fan-in
* `WebUi/Abstractions/VaultEvents.cs` — `IVaultEvents` + `VaultEventRelay`
* `Mobile/Services/VaultPushClient.cs` — phone client
* `Mobile/Components/ConnectGuard.razor` — connection lifecycle + lock routing
