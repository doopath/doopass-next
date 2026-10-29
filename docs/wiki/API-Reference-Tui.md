# API Reference — Doopass.Tui

> Generated from the compiler's XML documentation (`GenerateDocumentationFile=true`). Do not edit by hand: run `python scripts/generate-api-docs.py` after changing public members.

Assembly: `Doopass.Tui` · Source: `src/Doopass.Tui/bin/Debug/net10.0/Doopass.Tui.xml`

## Types

* [`Doopass.Tui.DoopassContext`](#doopasscontext)
* [`Doopass.Tui.DoopassTui`](#doopasstui)
* [`Doopass.Tui.DoopassWindow`](#doopasswindow)
* [`Doopass.Tui.InitStatus`](#initstatus)
* [`Doopass.Tui.LoginStatus`](#loginstatus)
* [`Doopass.Tui.Screens.BackupScreen`](#backupscreen)
* [`Doopass.Tui.Screens.ConfirmScreen`](#confirmscreen)
* [`Doopass.Tui.Screens.HelpScreen`](#helpscreen)
* [`Doopass.Tui.Screens.InitStoreScreen`](#initstorescreen)
* [`Doopass.Tui.Screens.LoginScreen`](#loginscreen)
* [`Doopass.Tui.Screens.MainMenuScreen`](#mainmenuscreen)
* [`Doopass.Tui.Screens.MessageScreen`](#messagescreen)
* [`Doopass.Tui.Screens.PairFormScreen`](#pairformscreen)
* [`Doopass.Tui.Screens.ScreenBase`](#screenbase)
* [`Doopass.Tui.Screens.SettingsScreen`](#settingsscreen)
* [`Doopass.Tui.Screens.StoreScreen`](#storescreen)
* [`Doopass.Tui.Screens.ValueFormScreen`](#valueformscreen)
* [`Doopass.Tui.Theme.Monochrome`](#monochrome)
* [`Doopass.Tui.Ui.FocusRing`](#focusring)
* [`Doopass.Tui.Ui.KeyRouteContext`](#keyroutecontext)
* [`Doopass.Tui.Ui.KeyboardLayout`](#keyboardlayout)
* [`Doopass.Tui.Ui.TuiAction`](#tuiaction)
* [`Doopass.Tui.Ui.TuiKeyMap`](#tuikeymap)
* [`Doopass.Tui.Ui.UiKit`](#uikit)

## `Doopass.Tui`

### `DoopassContext`

Shared state for the TUI: settings, the currently unlocked store and
backup helper.

* `CurrentStore` — The unlocked store, or `null` when logged out.
* `ReloadStore()` — Re-reads the store from disk (e.g. after restoring a backup).

### `DoopassTui`

Entry point for the Terminal.Gui (TUI) front-end.

* `Run()` — Runs the TUI application and blocks until it exits.

### `DoopassWindow`

The root window: header bar, screen stack, footer bar and the single global
key route. Screens never handle Tab, Esc, Q or F1 themselves.

* `Back()` — Returns to the previous screen.
* `Center(string, int)` — Pads text so it is centred on a fixed-width row.
* `CloseAndReplace(ScreenBase)` — Closes the current screen and its parent, then shows `screen`.
* `Confirm(string, Action)` — Shows a yes/no confirmation; the callback runs when Yes is pressed.
* `FocusCurrent()` — Re-asserts the screen's own tab order. The application focuses the first focusable element when a runnable starts (the window's content container), so this runs on the first main-loop iteration.
* `FocusStop(View)` — Focuses a control and clears the previous one. Views live in different containers (the screen and framed panels), so SetFocus alone can leave two controls marked as focused at the same time.
* `FocusedViewForTests()` — The deepest view that has focus. `Navigation` can report the focused container instead of the focused control, which would make "is the user typing / is a list focused" checks wrong.
* `GotoMain()` — Clears the stack and returns to the main menu.
* `Message(string, Action)` — Shows a message screen; the callback runs when OK is pressed.
* `Push(ScreenBase)` — Pushes a screen on top; the previous screen stays alive for `Back`.
* `Quit()` — Exits the application.
* `RefreshChrome()` — Re-centres the header and footer for the current screen width.
* `Replace(ScreenBase)` — Navigates forward: the current screen becomes the back target (Esc returns to it) unless it is a transitional entry screen such as sign-in.
* `RouteKey(Key, IApplication)` — Called from `KeyDown`, i.e. before any view sees the key. Consumes only the actions in `TuiAction`.

### `InitStatus`

Result of an init-store attempt.

* `AlreadyExists` — A store file already exists.
* `InvalidPassword` — The password failed `PasswordValidator`.
* `Success` — The store was created and unlocked.

### `LoginStatus`

Result of a login attempt.

* `IncorrectPassword` — The password did not decrypt the store.
* `NotInitialized` — No store file exists yet.
* `Success` — The store was decrypted and is now unlocked.

## `Doopass.Tui.Screens`

### `BackupScreen`

Backup management: create, restore, delete.

* `RunShortcut(char)` — Hotkeys routed from `DoopassWindow`: c create, r restore, d delete.

### `ConfirmScreen`

A minimal monochrome yes/no confirmation.

*No documented members.*

### `HelpScreen`

The help screen (hotkeys and about). Dismiss with Back, Esc or F1.

*No documented members.*

### `InitStoreScreen`

The init-store (sign up) screen.

*No documented members.*

### `LoginScreen`

The log-in screen (asks for the master password).

* `UpdateLayoutHint(string)` — Shows which keyboard layout produced the last typed character, so a user who typed the password in the wrong layout can see why it fails.

### `MainMenuScreen`

The main menu: log in, init store, backups, settings, exit.

*No documented members.*

### `MessageScreen`

A minimal monochrome message screen.

*No documented members.*

### `PairFormScreen`

A reusable key/value entry form (add pair, update pair).

*No documented members.*

### `ScreenBase`

Base class for all TUI screens.

* `FooterHint` — Contextual hotkeys for the footer bar.
* `KeepInHistory` — Whether this screen stays available as a back target when it is replaced. Entry screens (sign in, init store) are transitional: going back to them is meaningless, so they are dropped and the screen below them becomes "back".
* `ScreenTitle` — Text for the header bar.
* `TabStops` — Controls in the order Tab/Shift+Tab should visit them.
* `AddStop``1()` — Registers a focusable control in the screen's tab order.
* `CenterView(View, int)` — Centres a fixed-size view after each layout pass. Computing the offset here avoids `Pos.Center() - width/2`, which evaluates against a zero-width superview during construction and leaves the view clipped.
* `CenterView(View, int, int)` — Centres a fixed-size view on both axes (dialogs and forms).
* `FocusInitial()` — Gives focus to the first tab stop on this screen.
* `OnFocusMoved(View)` — Called when the shell moves focus to a control on this screen.
* `OnShown()` — Called every time this screen becomes the visible one.

### `SettingsScreen`

Settings: every section is a group of "key = value" rows.

* `_rows` — Maps a visible list row to a setting; null for section headers.
* `RestoreDefaults()` — Hotkey routed from `DoopassWindow`: 0 restores defaults.

### `StoreScreen`

The store screen: a search box, a scrolling list of keys and a details pane
for the selected secret. Actions live on hotkeys; Tab only switches between
search and the list.

* `RunShortcut(char)` — Hotkeys routed from `DoopassWindow` while the list has focus.

### `ValueFormScreen`

A single-value edit form (used for settings).

*No documented members.*

## `Doopass.Tui.Theme`

### `Monochrome`

A monochrome scheme: static black background, white foreground, and an
inverted (white-on-black) highlight for the focused element. Only the
secondary "dim" scheme uses gray, for hints and footers.

*No documented members.*

## `Doopass.Tui.Ui`

### `FocusRing`

Index arithmetic for an explicit, per-screen tab order.

*No documented members.*

### `KeyRouteContext`

Everything the mapping needs to know about the current focus.
`IsReadOnlyText` is a scrollable pane (the secret details):
it keeps page keys for scrolling but must still be left with H/L.

* `IsEditableText` — An editable field: every printable letter belongs to it.
* `KeyRouteContext(bool, bool, bool, bool)` — Everything the mapping needs to know about the current focus. `IsReadOnlyText` is a scrollable pane (the secret details): it keeps page keys for scrolling but must still be left with H/L.

### `KeyboardLayout`

Best-effort keyboard layout detection for the sign-in screen. Windows exposes the
active layout of the calling thread; elsewhere we fall back to the script of the
last typed character (see `ForText(string)`).

* `Current()` — The layout that is active right now, as "RU" / "EN" / a two-letter code.
* `ForText(string)` — The layout that produced the last character of `text`.

### `TuiAction`

What a global key press means, independent of Terminal.Gui.

*No documented members.*

### `TuiKeyMap`

Pure key mapping. Global hotkeys are resolved here so they can be unit tested
without an application, a terminal or a timer.

* `Resolve(Key, KeyRouteContext)` — Actions available on the store screen when the list has no focus.

### `UiKit`

Stateless factory for the visual building blocks shared by all screens.
Focus order is owned by each screen (see ScreenBase), never by a static registry.

* `ContentTop` — Rows reserved above the screen content (header bar).
* `FooterRows` — Rows reserved below the screen content (footer bar + gap).
* `ListWidth` — Width of the list column on the store screen.
* `CenterActions(FrameView, int, int, int, View, Int32}[])` — Centres a row of fixed-width actions inside a framed panel. Uses the panel's declared outer width (the rounded border eats one column per side) because `panel.Viewport.Width` differs between layout passes and would leave the row one column off. Applying it from `SubViewLayout` keeps it correct after a resize.
* `Detail(Pos, Pos, Dim, Dim)` — Read-only, wrapped, scrollable text pane (the secret detail view).
* `Empty(string)` — Text for an empty list.
* `List(String})` — A scrolling list. Arrow keys, PgUp/PgDn and Home/End are handled natively by ListView; J/K are bound per instance so they never collide with the global key router.
* `MenuItem(string, Pos, Pos, Action, int)` — A menu row: a focusable, full-width label. Used where a ListView's internal horizontal scrolling would clip long, centered rows.
* `Panel(string, Pos, Pos, Dim, Dim)` — A framed panel; children position themselves relative to the frame body.
* `PlainInput(Pos, Pos, int, string)` — A bare single-line input, used inline (e.g. the store filter row).
* `Primary(string, Pos, Pos, Action, int)` — A flat, one-line action item. Terminal.Gui's Button draws decorative glyph boxes that do not fit a dense monochrome layout, so actions are focusable labels: the focused one simply inverts.
