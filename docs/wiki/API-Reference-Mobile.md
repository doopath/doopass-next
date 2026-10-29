# API Reference — Doopass.Mobile

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `Doopass.Mobile` · Source: `src/Doopass.Mobile/bin/Debug/net10.0-android/Doopass.Mobile.xml`

## Types

* [`Doopass.Mobile.App`](#app)
* [`Doopass.Mobile.Platforms.Android.MainActivity`](#mainactivity)
* [`Doopass.Mobile.Resource`](#resource)
* [`Doopass.Mobile.Services.AndroidClipboard`](#androidclipboard)
* [`Doopass.Mobile.Services.ConnectionStore`](#connectionstore)
* [`Doopass.Mobile.Services.HostCandidate`](#hostcandidate)
* [`Doopass.Mobile.Services.HostConnectionState`](#hostconnectionstate)
* [`Doopass.Mobile.Services.HostDiscovery`](#hostdiscovery)
* [`Doopass.Mobile.Services.MulticastLockHandle`](#multicastlockhandle)
* [`Doopass.Mobile.Services.RemoteVaultSession`](#remotevaultsession)
* [`Doopass.Mobile.Services.VaultPushClient`](#vaultpushclient)
* [`Doopass.Mobile.Vault`](#vault)

## `Doopass.Mobile`

### `App`

Application shell: one window showing the Blazor WebView page.

* `App(IServiceProvider)` — Application shell: one window showing the Blazor WebView page.

### `Resource`

Android Resource Designer class.
Exposes the Android Resource designer assembly into the project Namespace.

*No documented members.*

### `Vault`

The single content page: a BlazorWebView rooted at the mobile app router
component (ConnectGuard + RCL Routes with the Connect page attached).

* `Vault(IServiceProvider services)` — Creates the page that hosts the Blazor WebView.
  * `services` — App service provider (the WebView resolves components from it).

## `Doopass.Mobile.Platforms.Android`

### `MainActivity`

Single MAUI activity hosting the BlazorWebView. Android back is left to the
WebView's history handling; only system-level myths like closing the app
from "/connect" get tuned later if real devices show a problem.

*No documented members.*

## `Doopass.Mobile.Services`

### `AndroidClipboard`

Android clipboard. The WebView path (navigator.clipboard / execCommand) is
blocked because the copy happens after an awaited vault read, so the user
gesture is gone; ClipboardManager has neither requirement. The clip is also
flagged as sensitive so Android 13+ does not preview the secret.

*No documented members.*

### `ConnectionStore`

Persisted connection configuration of the thin client on this phone:
the host URL, the pinned certificate fingerprint (trust-on-first-use) and
the session token. Everything lives in SecureStorage (Android Keystore);
there is no local vault on the phone — only the address, the pin and the
token of the host.

* `Host` — Saved base URL of the host, or `null` when none is paired.
* `HostUrl` — Base URL of the host, e.g. `http://192.168.1.10:7240`.
* `IsConfigured` — True when a host address has been saved on this device.
* `PinnedFingerprint` — SHA-256 hex fingerprint of the pinned TLS certificate; null for plain HTTP.
* `Token` — Session token issued by the host, or `null` when logged out.
* `ClearAsync()` — Forgets everything (host, pin, token).
* `LoadAsync()` — Loads stored configuration at application start.
* `SaveHostAsync(string, string)` — Saves host + pin; clears the old token (a fresh login follows).
* `SetTokenAsync(string token)` — Stores or clears the session token (persisted in SecureStorage).
  * `token` — New token, or `null` to forget it.

### `HostCandidate`

A host found on the LAN (or typed in by hand on the connect screen).

* `HostCandidate(string, string, bool, string, string)` — A host found on the LAN (or typed in by hand on the connect screen).

### `HostConnectionState`

Exposes the host saved by `ConnectionStore` to the shared UI and
remembers whether the last call reached it. Registered only by the thin
client, so the web login screen has no host row and never shows the offline
banner.

* `HostConnectionState(ConnectionStore connections)` — Creates the descriptor over the device's saved connection state.
  * `connections` — Persisted host, pin and token.

### `HostDiscovery`

Finds Doopass hosts in the LAN with a UDP broadcast (matching the host's
discovery service): sends `DOOPASS_DISCOVER` to port 7241 and collects
replies for ~1.5 s, deriving the host address from each reply's sender.
The probe goes to both 255.255.255.255 and the directed broadcast of every
IPv4 interface, because some Wi-Fi stacks (and default routes through a VPN)
drop the global broadcast. Requires a multicast lock on Wi-Fi so broadcast
responses are not filtered.

* `AnnouncePort` — UDP port the host listens on for discovery probes.
* `BroadcastTargets()` — Global broadcast plus the directed broadcast of each active IPv4 interface (e.g. 192.168.1.255 for 192.168.1.42/24).
* `DiscoverAsync(TimeSpan} window, CancellationToken ct = default)` — Broadcasts the discovery probe and collects host replies for the given window (1.5 s by default). Never throws; failures are logged and an empty list is returned.
  *Returns:* One candidate per answering host, de-duplicated by URL.
  * `window` — How long to listen for replies.
  * `ct` — Cancellation token.

### `MulticastLockHandle`

MulticastLock itself is not IDisposable; release it on dispose.

* `MulticastLockHandle(MulticastLock)` — MulticastLock itself is not IDisposable; release it on dispose.

### `RemoteVaultSession`

IVaultSession for the thin client: every call is an HTTP request against the
host REST API (Doopass.Cli.Api). HTTP statuses map back to Core exceptions so
the shared RCL pages behave exactly like the local web version. Secret
responses are never logged and never cached.

* `RemoteVaultSession(ConnectionStore connections, HostConnectionState host, VaultEventRelay relay)` — Creates the remote session over the phone's saved connection state.
  * `connections` — Persisted host, pin and token.
  * `host` — Reachability tracker shared with the login screen.
  * `relay` — Per-app event relay for lock notifications.

### `VaultPushClient`

SignalR push channel to the host hub: keeps open pages fresh. The token
rides the standard access_token provider; a lock clears the stored token
and raises the relay's VaultLocked so the guard navigates back to the
connect / login flow.

* `IsCurrentSession` — True when the live connection belongs to the saved host and token. After the host is changed, the old connection is still open for a moment and must not be mistaken for the channel of the new session.
* `VaultPushClient(ConnectionStore connections, VaultEventRelay relay)` — Creates the push client over the device's saved host and token.
  * `connections` — Persisted connection state.
  * `relay` — Per-app event relay the hub events are forwarded to.
* `DisposeAsync()` — Stops the hub connection and releases the start gate.
  *Returns:* A task that completes when the connection is closed.
* `StartAsync()` — Starts (or restarts) the hub connection for the current session.
