<div align="center">

  <img src="assets/logo.png" alt="Doopass logo" width="104" />

  # Doopass

  **A cross-platform password manager for the terminal and the browser — rewritten in .NET 10.**

  [![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
  [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-blue)](#run-it)
  [![Frontends](https://img.shields.io/badge/frontends-TUI%20%2B%20Web%20(PWA)-4f46e5)](#run-it)
  [![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](https://github.com/doopath/PasswordManager/blob/master/LICENSE)

</div>

Doopass keeps everything in a single encrypted `store.enc` file and offers two
front-ends for it:

* a **TUI** built on Terminal.Gui v2 that mirrors the original Textual interface;
* a **custom-styled web UI** (ASP.NET Core Blazor Server) with light and dark
  themes, served as a local web app.

This project is a from-scratch .NET 10 rewrite of the original
[Doopass](https://github.com/doopath/PasswordManager/) (Python + Textual). The
encrypted store format is **fully compatible**, so existing files open without
any migration — just point the new app at the same `~/doopass` folder.

## Features

- **Drop-in compatible** with the original Python implementation: Fernet crypto
  (AES-128-CBC + HMAC-SHA256) derived with PBKDF2-HMAC-SHA256 (100 000 iterations).
- **Two front-ends, one store** — the same vault is available in the terminal
  and in the browser, with dated backups, restore and settings in both.
- **Self-contained single-file executables** for Windows and Linux — no .NET
  runtime required, and all web assets are embedded in the binary.
- **Installable web app (PWA)** — opens as its own window on desktop and mobile,
  with a basic offline shell and its own icons.
- **Launch at sign-in** — helper scripts install a Windows startup shortcut or a
  `systemd` user service.
- **Plain, honest data** — everything lives in `~/doopass/appdata`, the log file
  never records secrets, and it rotates at 5 MB.

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/) or newer

## Build and test

```bash
dotnet build Doopass.slnx
dotnet test Doopass.slnx                 # all test projects
```

## Run it

### Terminal UI

```bash
dotnet run --project src/Doopass.Cli
```

Hotkeys (press <kbd>F1</kbd> any time for the built-in help):

| Keys                                | Action                                                    |
| ----------------------------------- | --------------------------------------------------------- |
| <kbd>J</kbd>/<kbd>K</kbd> or <kbd>↑</kbd>/<kbd>↓</kbd> | move the selection, scroll the list       |
| <kbd>H</kbd>/<kbd>L</kbd>           | move focus left / right between regions                    |
| <kbd>Enter</kbd>                    | select, copy the secret, submit the form                   |
| <kbd>Tab</kbd>/<kbd>Shift+Tab</kbd> | move focus between the list, fields and buttons            |
| <kbd>PgUp</kbd>/<kbd>PgDn</kbd>     | scroll a list or a value by a page                         |
| <kbd>Home</kbd>/<kbd>End</kbd>      | jump to the first / last item                              |
| <kbd>V</kbd>                        | reveal or hide the selected value (store screen)           |
| <kbd>E</kbd> / <kbd>D</kbd> / <kbd>A</kbd> | edit, delete or add a pair (store screen)           |
| <kbd>C</kbd> / <kbd>R</kbd> / <kbd>D</kbd> | create, restore or delete a backup                  |
| <kbd>0</kbd>                        | restore default settings (settings screen)                 |
| <kbd>G</kbd>                        | lock the vault: forget the password, back to the menu      |
| <kbd>Esc</kbd>                      | go back one screen                                         |
| <kbd>Q</kbd>                        | quit Doopass                                               |

### Web UI

```bash
dotnet run --project src/Doopass.Cli -- serve            # http://localhost:7240
dotnet run --project src/Doopass.Cli -- serve --port 7240 --lan
```

`serve` options:

| Option                        | Description                                     |
| ----------------------------- | ----------------------------------------------- |
| `--port N` (`-p`)             | HTTP port (default `7240`)                      |
| `--no-browser`                | do not open the browser automatically           |
| `--lan` (`--network`)         | listen on `0.0.0.0` instead of `localhost`      |

The UI and all assets are embedded in the executable, so nothing else is needed
to serve the app. Open <http://localhost:7240> and log in or initialize a store.

#### Install as an app (PWA)

The web interface is an installable Progressive Web App — installed apps run in
their own window, like a desktop application:

* **Chrome / Edge** — Settings → *Install as app*, or the install icon in the
  address bar.
* **iOS (Safari 16.4+)** — Share → *Add to Home Screen*.
* **Android** — Chrome or Samsung Internet: *Install app* / *Add to Home screen*
  (creates a real WebAPK with the Doopass icon).
* **Safari 17 (macOS)** — File menu → *Add to Dock*.
* **Firefox desktop** does not support installing web apps; mobile Firefox adds
  only a home-screen shortcut.

The app ships its own manifest, service worker and icons, all embedded in the
executable and served from virtual routes (`/pwa/manifest.webmanifest`,
`/pwa/sw.js`, …) — a single-file build supports installation with no extra
files. The service worker offers a basic offline shell; vault operations still
need the server and reconnect over their existing SignalR circuit.

> [!IMPORTANT]
> Installation requires a **secure origin**: `http://localhost` or any HTTPS
> host. A plain-HTTP LAN address (`--lan` without TLS) can use the UI but
> cannot install the app.

To regenerate the icons after a design change:

```bash
python scripts/generate-icons.py --out .pwa-assets    # PNGs are embedded from there via the csproj
```

## Deploy it

### Portable single-file build

```powershell
scripts\publish-portable            # or: powershell -File scripts\publish-portable.ps1
```

Produces one self-contained executable per platform (no .NET install needed):

* `dist/portable/win-x64/doopass.exe`
* `dist/portable/linux-x64/doopass`

Run it directly:

```bash
dist\portable\win-x64\doopass.exe serve --port 7240     # Windows
chmod +x dist/portable/linux-x64/doopass                # Linux
./dist/portable/linux-x64/doopass serve                 # Linux
```

### Release archives

```powershell
scripts\package-release             # after publish-portable
```

Builds archives ready for GitHub Releases:

* `dist/release/doopass-5.0.0-win-x64.zip` (exe + install/uninstall scripts + README)
* `dist/release/doopass-5.0.0-linux-x64.tar.gz` (exe + install/uninstall scripts + systemd unit + README)

### Autostart at sign-in

The installers take the executable path as the first argument and copy it to
`~/.local/bin` (`%USERPROFILE%\.local\bin` on Windows).

**Windows**

```powershell
powershell -File scripts\install-autostart-win.ps1 .\doopass.exe            # localhost
powershell -File scripts\install-autostart-win.ps1 .\doopass.exe lan        # LAN
powershell -File scripts\uninstall-autostart-win.ps1
```

Options: `-Port 7240`, `-DryRun`, `-NoDesktopShortcut`.

**Linux**

```bash
scripts\install-autostart-linux.sh ./doopass                 # localhost
scripts\install-autostart-linux.sh ./doopass lan [--linger]  # LAN
scripts\uninstall-autostart-linux.sh
```

Installs a `systemd` **user service** (`~/.config/systemd/user/doopass.service`)
started with `enable --now`.

> [!WARNING]
> `lan` mode listens on all network interfaces over plain HTTP and is only
> appropriate on a trusted network.

## Where your data lives

By default everything uses `~/doopass/appdata` — the same location as the
original Python implementation — so an existing `store.enc` and
`settings.json` are picked up automatically. Override the root with the
`DOOPASS_HOME` environment variable.

```
~/doopass/
└── appdata/
    ├── store.enc        # encrypted store
    ├── settings.json    # application settings
    ├── backups/         # dated store backups
    └── doopass.log      # log file
```

Application events (start/stop, store and backup operations, settings changes,
errors) are appended to `doopass.log`, which rotates at 5 MB.

> [!NOTE]
> Secrets are never written to the log.

## The store file

The on-disk format is byte-for-byte compatible with the original Doopass:

```
base64url( salt[16] || iterations[4, big-endian] || fernet_token )
```

where `fernet_token = 0x80 || timestamp[8] || IV[16] || AES-128-CBC(ciphertext) || HMAC-SHA256(32)`.
The plaintext is a newline-separated list of `key = value` pairs, encrypted
with a key derived from your master password (PBKDF2-HMAC-SHA256, 100 000
iterations by default). The master password is kept in memory only and erased
on lock (<kbd>G</kbd>), exit or server shutdown.

## Project layout

| Project                      | Description                                                     |
| ---------------------------- | --------------------------------------------------------------- |
| `src/Doopass.Core`           | Store, Fernet/PBKDF2 crypto, settings, backups, validation, logs |
| `src/Doopass.Tui`            | Terminal.Gui v2 interface (mirrors the original TUI)             |
| `src/Doopass.Cli`            | Single entry point: the TUI or the Blazor Server web host        |
| `tests/Doopass.Core.Tests`   | xUnit tests: format compatibility, CRUD, backups, settings       |
| `tests/Doopass.Tui.Tests`    | xUnit tests: key mapping and screen behaviour                    |

---

Doopass is a rewrite of the original [Doopass](https://github.com/doopath/PasswordManager/)
and follows the same license (GPL-3.0).
