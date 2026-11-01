# Application: Web UI (`Doopass.WebUi`)

A Razor Class Library shared by the browser thin client (`Doopass.WebClient`)
and the Android thin client. Everything here is a Blazor component, an asset, or
a contract — no ASP.NET Core hosting, no HTTP, no platform APIs. The host page
(`index.html`) and the router (`App.razor`) live in `Doopass.WebClient`; the
phone supplies its own `AppRoot.razor`.

```
Doopass.WebUi
├── Abstractions/
│   ├── IVaultSession.cs        vault operations contract
│   ├── VaultEvents.cs          IVaultEvents + VaultEventRelay + VaultState
│   ├── IHostConnection.cs      optional remote-host descriptor
│   ├── ISecretClipboard.cs     clipboard contract
│   └── HostUnreachableException.cs
├── Components/
│   ├── Routes.razor            router used by the Android host
│   ├── Layout/MainLayout.razor sidebar, topbar, ErrorBoundary, lock handling
│   ├── Layout/AuthLayout.razor centred card for login / init
│   ├── Pages/Login.razor       "/"
│   ├── Pages/InitStore.razor   "/init"
│   ├── Pages/Store.razor       "/store"
│   ├── Pages/Backups.razor     "/backups"
│   ├── Pages/Settings.razor    "/settings"
│   ├── Pages/Error.razor, NotFound.razor
│   └── Ui/                     Icon, Modal, ToastHost, ThemeToggle
├── Services/                   JsClipboard, ToastService
└── wwwroot/                    app.css, app.js, theme.js, brand.svg
```

## Routes

| Route | Page | Layout | Guard |
| ----- | ---- | ------ | ----- |
| `/` | Login | AuthLayout | redirects to `/store` when unlocked |
| `/init` | InitStore | AuthLayout | no guard; store must not exist |
| `/store` | Store | MainLayout | not unlocked → `/` |
| `/backups` | Backups | MainLayout | not unlocked → `/` |
| `/settings` | Settings | MainLayout | not unlocked → `/` |
| `/connect` | (Android only, `Doopass.Mobile/Components/Connect.razor`) | ConnectLayout | — |

The shared library never references the mobile project, so it knows nothing
about `/connect`; the phone adds that route in its own router.

## `IVaultSession` at work

Pages inject `IVaultSession` and `IVaultEvents` and do not care which host they
run in:

```csharp
if (!(await Session.GetStateAsync()).IsUnlocked) { Nav.NavigateTo("/"); return; }
_keys = (await Session.GetKeysAsync()).ToList();
Events.VaultChanged += OnVaultChanged;   // re-read on every push
```

Backup names are extensionless on both sides (`GetBackupNamesAsync`,
`RestoreBackupAsync(name)`, `DeleteBackupAsync(name)`).

## Pages

### Login (`/`)

* Renders host row + "Change host" **only** when `IHostConnection` is
  registered (phone). `EnsureLoadedAsync()` is awaited because the page may
  render before the route guard loads the saved host.
* Offline banner when `IHostConnection.IsUnreachable` and an address exists.
* On submit: `LoginAsync`; on failure it re-reads state to distinguish
  *offline* (banner only, no misleading password error), *store missing*
  (redirect `/init`) and *wrong password* (inline message).
* `Enter` submits; the button shows a spinner and "Unlocking…" while busy.
* `HostUnreachableException` is swallowed into the banner path.

### Init store (`/init`)

* Two password fields, client-side equality check, server/validator message
  shown inline.
* Success navigates to `/store`; the vault is unlocked by the init call itself.

### Store (`/store`)

* Header with item count (`"N items"` or `"S of N shown"`).
* Search filters keys case-insensitively; keystrokes are debounced 250 ms before
  a re-render, because every keystroke otherwise round-trips over SignalR and
  lags mobile keyboards.
* Cards show the first letter, the key, a masked value and four actions:
  **reveal**, **copy**, **edit**, **delete**.
* Values are fetched lazily on reveal and cached only while revealed; hiding or
  editing removes them from the cache, so a secret does not sit in the DOM
  longer than asked.
* Copy goes through `ISecretClipboard`; a platform refusal (insecure context, no
  user gesture) reveals the value and shows a toast instead of failing silently.
* Add/edit uses a modal; empty keys are rejected, `PropertyAlreadyExistError` /
  `PropertyDoesNotExistError` are shown inline.
* Delete requires confirmation.
* `VaultChanged` triggers `ReloadAsync` + re-read of keys and of currently
  revealed values, so a change from another client appears without a page
  reload.

### Backups (`/backups`)

* Lists snapshots newest-first with **Restore** and **Delete** actions; restore
  is confirmed and explains that a safety backup is written first.
* Subscribes to both `BackupsChanged` and `VaultChanged` (a restore raises both).
* Toasts report success/failure; the list refreshes in `finally`.

### Settings (`/settings`)

* Renders every section returned by `GetSettingsAsync` as a card with
  `key = value` rows and a per-row edit modal; "Restore defaults" is confirmed
  and warns that the vault is unaffected.
* Section subtitles are mapped in `SectionSubtitle` (`local` → storage paths).
* "Install as app" card calls `doopass.pwa.probe` once after the first render and
  maps the result to guidance: `standalone`, `installable` (installs through the
  captured `beforeinstallprompt`), `ios`, `manual`, `no-service-worker`.

## Layout and chrome

`MainLayout` provides the sidebar (Store / Backups / Settings), the "Local &
encrypted" chip, the theme toggle, the logout button, the version string, and a
mobile hamburger with a backdrop. On every navigation the sidebar closes — on a
phone it covers the whole screen and a second tap used to look like "nothing
happened".

`VaultLocked` from `IVaultEvents` navigates back to `/`. The layout also
subscribes to `NavigationManager.LocationChanged` and uses `ErrorBoundary` so a
component exception shows a reload card instead of a dead page.

`AuthLayout` is the centred card used by login and init.

## Assets and progressive enhancement

### `theme.js`

* Reads `localStorage["doopass-theme"]`; falls back to
  `prefers-color-scheme`.
* Applies `data-theme="dark|light"` on `<html>` **before paint** and keeps the
  `theme-color` meta in sync (`#0d0f15` / `#f6f7fb`).
* Re-asserts the theme with a `MutationObserver`, `visibilitychange` and
  `pageshow`, because Blazor can strip the attribute during navigation.
* Exposes `window.doopassTheme = { toggle, isDark, set, current, restore }`.

### `app.js`

* `doopass.copyText(text)` → `navigator.clipboard.writeText`, falling back to a
  hidden textarea + `execCommand('copy')`; returns `false` instead of throwing.
* PWA helpers: `beforeinstallprompt` capture, `isStandalone`, `canInstall`,
  `promptInstall`, `probe`, and service-worker registration with scope `/`.
  Registration failures are silent.

### `app.css`

A single hand-written stylesheet (~1.9k lines) with CSS custom properties for
both themes, responsive breakpoints, safe-area insets and the component classes
used by the pages (`card`, `item-card`, `btn`, `input`, `modal`, `toast`,
`auth-*`, `connect-*`). No CSS framework and no build step.

### `Icon.razor`

Inline SVG icon set with `Name`, `Size`, `StrokeWidth` and `Class`
parameters. Available names: `add`, `close`, `chevron`, `check`, `eye`,
`eye-off`, `copy`, `edit`, `delete`, `lock`, `moon`, `sun`, `key`, `history`,
`settings`, `help`, `logout`, `menu`, `search`, `restore`, `shield`, `warning`,
`info`, `success`, `clock`, `database`. Unknown names render an empty SVG.
Adding an icon is a new arm of the `Name switch` expression.


## UI primitives

| Component | Purpose |
| --------- | ------- |
| `Modal` | accessible dialog with `Visible`, `Title`, `ChildContent`, `Footer`, `OnClose` |
| `ToastHost` | renders `ToastService.Toasts`, subscribes to `Changed` |
| `ThemeToggle` | calls `doopassTheme.toggle()` and tracks the current mode |
| `Icon` | inline SVG set |

`ToastService` is scoped, keeps toasts in memory and auto-dismisses each after
3.8 s. Kinds: `Success`, `Error`, `Info`.

## App shell (`Doopass.WebClient`)

* `wwwroot/index.html` carries `viewport-fit=cover` + safe-area CSS, the SVG
  favicon, the manifest, the Apple touch icon and the `theme.js`/`app.js`
  includes; `App.razor` only mounts the router over the shared pages.
* The Blazor WebAssembly runtime (`_framework/blazor.webassembly.js`) is loaded
  at the end of `<body>`. There is no server circuit, no prerender and no
  reconnect modal: a lost host is reported by the session/offline banner.
* Login/init call `/api/auth/*` directly; the token goes to `sessionStorage`.
* The PWA registration and install prompt come from `app.js`.

## Conventions when editing the UI

1. Pages talk to `IVaultSession`/`IVaultEvents` only — never to `Store`,
   `Settings` or HTTP directly. The TUI is the only front-end that skips the
   contract.
2. Unsubscribe in `Dispose`; every page that subscribes also checks the state in
   `OnInitializedAsync`.
3. Secrets stay out of markup unless the user revealed them; never bind a value
   into a hidden input "for convenience".
4. Use `ToastService` for operation results and inline `div.error` for form
   validation.
5. Keep the mobile layout in mind: tap targets ≥ 40 px, no hover-only actions,
   and no keyboard-dependent flows.
