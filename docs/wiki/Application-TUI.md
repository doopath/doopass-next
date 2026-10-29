# Application: TUI (`Doopass.Tui`)

A Terminal.Gui v2 interface (`Terminal.Gui` 2.4.17) that mirrors the original
Textual UI. It runs in-process against `Doopass.Core`, with no HTTP, no server
and no background tasks.

```
src/Doopass.Tui
├── DoopassTui.cs        entry point: bootstrap, global key hook, run loop
├── DoopassWindow.cs     root window: header/footer, screen stack, key router
├── DoopassContext.cs    settings, unlocked store, backup helper
├── Screens/             MainMenu, Login, InitStore, Store, Backup, Settings,
│                        Help, Message, Confirm, PairForm, ValueForm, ScreenBase
├── Ui/                  UiKit, TuiKeyMap, FocusRing, KeyboardLayout
└── Theme/Monochrome.cs  colour schemes
```

## Startup

```csharp
ConfigurationManager.Enable(ConfigLocations.All);
var settings = new Settings();
AppLogger.Initialize(settings.LogFile);
var context = new DoopassContext(settings);
using var app = Application.Create();
app.Init();
Monochrome.Register();
var window = new DoopassWindow(context);
app.Keyboard.KeyDown += (_, key) => window.RouteKey(key, app);
app.Iteration += ... FocusCurrent();       // focus the screen's first control
app.Run(window);
```

The key hook is registered on `IKeyboard.KeyDown`, i.e. **before** any focused
view sees the key, so global hotkeys win over ListView/TextField bindings.

## Screen model

`ScreenBase : View` provides:

| Member | Purpose |
| ------ | ------- |
| `ScreenTitle` | header bar text |
| `FooterHint` | contextual hotkey line |
| `KeepInHistory` | `false` for sign-in/init screens so Esc never returns to them |
| `TabStops` / `AddStop` | explicit tab order; containers are not focus stops |
| `CenterView` | centres fixed-size panels after layout |
| `OnShown`, `OnFocusMoved`, `FocusInitial` | hooks used by the shell |

`DoopassWindow` keeps a `Stack<ScreenBase>`:

| Method | Semantics |
| ------ | --------- |
| `Replace(screen)` | drops the current screen unless it is a transitional entry screen, then shows the new one |
| `Push(screen)` | keeps the current screen as the Esc target (forms, dialogs, help) |
| `Back()` | pops one screen (no-op at the root) |
| `CloseAndReplace(screen)` | pops two screens and shows the new one (used after a successful form submit) |
| `GotoMain()` | clears the stack and returns to the main menu (vault lock) |
| `Message` / `Confirm` | convenience wrappers around `MessageScreen` / `ConfirmScreen` |
| `Quit()` | `App.RequestStop()` |

## Screens

| Screen | Behaviour |
| ------ | --------- |
| **Main menu** | ASCII logo, five rows; the first row is dynamic: "Log in" when locked, "Open store — unlocked" when a store is already in memory. Rows: login/open, init store, backups, settings, exit. |
| **Login** | master password field, "incorrect password" inline error, keyboard-layout hint under the button, Enter submits. Not kept in history. Missing store routes to init. |
| **Init store** | password + confirmation, mismatch message, validator message, "stored under ~/doopass/appdata/store.enc" footer. Not kept in history. |
| **Store** | search row (`search | field |`), scrolling key list, details pane (masked with `•`, length shown, `hidden`/`visible` badge), status line. `Enter` copies the secret, `v` reveals, `e` edits, `d` deletes (confirm), `a` adds. |
| **Backups** | list of snapshots, `c` create, `r` restore (confirm), `d` delete (confirm), status line with count and selection. Restoring reloads the store. |
| **Settings** | sections rendered as headers with `key  value` rows, `Enter` edits the selected row, `0` restores defaults (confirm). Section headers are not editable. |
| **Help** | static hotkey table + version, opened with `F1` from anywhere, closed with Esc/F1. |
| **Message / Confirm** | 60-column monochrome dialogs with centred action buttons. |
| **Pair form** | key + value entry used for add and update; Enter on the value submits. |
| **Value form** | single-value edit used by settings. |

## Global keys

Resolution order inside `RouteKey`:

1. `G` — lock the vault (drop the password, back to the main menu). Ignored
   while an editable text field has focus.
2. List navigation when a `ListView` is focused: `↑/↓`, `J/K`, PgUp/PgDn,
   Home/End, `Enter` (activate).
3. Detail pane scrolling when the read-only `TextView` is focused; at the
   top/bottom edge `J/K`/arrows move focus instead of trapping the cursor.
4. `0` on the settings screen — restore defaults.
5. `c`/`r`/`d` on the backups screen.
6. `v`/`e`/`d`/`a` on the store screen.
7. `TuiKeyMap.Resolve` — `Tab`/`Shift+Tab` (focus), `H`/`L` (focus left/right),
   `Esc` (back, or quit at the root), `F1` (help), `Q` (quit).

Editable text fields own every printable key, so typing `hjkl`, `q` or `g` into
a password or key field never triggers navigation.

## Layout

The window has no border and no shadow. Header (`DOOPASS ▸ <screen>`), a
divider, the content area inset by 2 columns, and a centred footer hint. Bars
are re-centred on `SubViewLayout` because the width is unknown during
construction.

The store screen uses `Dim.Fill(46)` for the list and a fixed 45-column details
panel anchored to the right.

## Theme

`Monochrome` registers three schemes (`Monochrome.SchemeName`,
`ChromeSchemeName`, `DimSchemeName`) and applies them at startup. There is no
colour theme selection in the TUI; the design is intentionally monochrome and
relies on bold/dim attributes.

## Keyboard layout hint

`KeyboardLayout.ForText` maps the last typed character to a two-letter layout
code (`RU` for Cyrillic, `EN` for Latin) and falls back to the active Windows
layout; the login screen prints e.g. `layout: EN` / `layout: RU` under the
button. It exists because a password typed in the wrong layout fails with
"incorrect password" and the hint makes that diagnosable.

## Focus handling

* `FocusRing.Next(current, count, forward)` computes the next tab stop with
  wrapping and is unit-tested.
* `ScreenBase.TabStops` is the screen's explicit order; Tab/Shift+Tab and H/L use
  it rather than Terminal.Gui's implicit tree order.
* `DoopassWindow.FocusCurrent()` runs on the first main-loop iteration, after
  layout and after the application has focused the container.

## Tests

`tests/Doopass.Tui.Tests` covers:

| File | Coverage |
| ---- | -------- |
| `TuiKeyMapTests` | global key mapping for every context combination |
| `FocusRingTests` | wrapping and empty-ring behaviour |
| `NavigationTests` | menu navigation, Tab traversal, Esc/back, shortcuts, locking |
| `ScreenDumpTests` | rendered screen text (labels, panels, hints) without a real terminal |

Screens are exercised through the real `DoopassWindow` and a fake driver, so a
change in focus routing is caught without manual typing.

## Known TUI constraints

* No mouse support beyond what Terminal.Gui provides for focus; the UI is
  keyboard-first.
* No clipboard abstraction: the TUI writes to the terminal clipboard via
  `app.Clipboard`, which needs OSC 52 support (or a local clipboard tool
  depending on the terminal).
* Values are trimmed by the storage layer, so spaces at the edges cannot be
  stored.
* The TUI never talks to the web host; it opens the same `store.enc` directly.
  Running both at once risks last-write-wins (see
  [Known Limitations](Known-Limitations.md)).
