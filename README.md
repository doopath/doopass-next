# Doopass (.NET 10)

A cross-platform rewrite of the **Doopass** password manager in **.NET 10**, with:

* a **TUI** front-end (Terminal.Gui v2) that mirrors the original Textual interface;
* a **modern, custom-styled web UI** (ASP.NET Core Blazor Server) with light/dark themes that runs as a local server.

The encrypted store format is **fully compatible** with the original Python implementation
(Fernet + PBKDF2-HMAC-SHA256), so existing `store.enc` files open without migration.

## Projects

| Project | Description |
|---|---|
| `src/Doopass.Core` | Store, Fernet/PBKDF2 crypto, settings, backups, password validation |
| `src/Doopass.Tui` | Terminal.Gui v2 interface (mirrors the original TUI) |
| `src/Doopass.Cli` | Single entry point: TUI, or the Blazor Server web host |
| `tests/Doopass.Core.Tests` | xUnit tests (format compatibility, CRUD, backups, settings) |

## Requirements

* [.NET SDK 10.0](https://dotnet.microsoft.com/) or newer

## Build

```bash
dotnet build Doopass.slnx
dotnet test tests/Doopass.Core.Tests/Doopass.Core.Tests.csproj
```

## Run the TUI

```bash
dotnet run --project src/Doopass.Cli
```

Hotkeys: `J`/`K` (or `Up`/`Down`) move the selection, `H`/`L` move focus left/right
between regions, `Enter` selects, `Tab`/`Shift+Tab` cycle all controls, `Esc` goes
back one screen, `Q` quits, `F1` opens help. On the store screen `V` reveals the
selected value, `E` edits, `D` deletes, `A` adds a pair; on backups `C` creates,
`R` restores, `D` deletes; `G` locks the vault and forgets the password; on settings `0` restores the
defaults; `PgUp`/`PgDn`/`Home`/`End`
scroll lists and long values.

## Run the web interface

```bash
dotnet run --project src/Doopass.Cli -- serve --port 7240
```

`serve` options:

* `--port N` — HTTP port (default `7240`)
* `--no-browser` — don't open the browser automatically
* `--lan` / `--network` — listen on `0.0.0.0` instead of `localhost` (LAN access)

The default binds to `localhost` only. Open <http://localhost:7240> and log in or
initialize a store. The web assets are embedded, so the app alone is enough to run.

## Install as an app (PWA)

The web interface is an installable Progressive Web App. Once Doopass is serving:

* **Chrome / Edge** — Settings page → “Install as app” → *Install Doopass…*, or the
  install icon in the address bar. Installs as its own window (like a desktop app).
* **iOS (Safari, 16.4+)** — Share → *Add to Home Screen*. Launches full-screen from
  the home-screen icon.
* **Android** — Chrome or Samsung Internet: “Install app” / “Add to Home screen”
  (creates a real WebAPK with the Doopass icon).
* **Safari 17 (macOS)** — File menu → *Add to Dock*.
* **Firefox desktop** does not support installing web apps; mobile Firefox can add
  only a home-screen shortcut.

The app requests the `standalone` display mode with `fullscreen` preferred when the
browser supports it, ships its own 192/512 px (regular + maskable) icons, an
apple-touch icon, and a service worker that offers a basic offline shell (start
screen and static assets load without the server; vault operations still need the
server and reconnect over their existing SignalR circuit).

Requirements and limits:

* Installation needs a **secure origin**: `http://localhost` or any HTTPS host.
  Plain-HTTP LAN addresses (`--lan` without TLS) can use the UI but cannot install.
* All PWA assets (manifest, service worker, icons) are embedded in the executable
  and served from virtual routes (`/pwa/manifest.webmanifest`, `/pwa/sw.js`, …), so
  the single-file build supports installation with no extra files.

To regenerate the icons after a design change:

```bash
python scripts/generate-icons.py --out .pwa-assets   # PNGs are embedded from there via the csproj
```

## Portable single-file build

```powershell
scripts\publish-portable            # or: powershell -File scripts\publish-portable.ps1
```

Produces one self-contained executable per platform (no .NET install required):

* `dist/portable/win-x64/doopass.exe`
* `dist/portable/linux-x64/doopass`

Run it directly:

```bash
dist\portable\win-x64\doopass.exe serve --port 7240     # Windows
chmod +x dist/portable/linux-x64/doopass                # Linux
./dist/portable/linux-x64/doopass serve                 # Linux
```

## Release packaging

```powershell
scripts\package-release             # after publish-portable
```

Builds archives ready for GitHub Releases:

* `dist/release/doopass-5.0.0-win-x64.zip` (exe + install/uninstall scripts + README)
* `dist/release/doopass-5.0.0-linux-x64.tar.gz` (exe + install/uninstall scripts + systemd unit + README)

## Autostart (web server at sign-in)

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

Installs a `systemd` user service (`~/.config/systemd/user/doopass.service`) started
with `enable --now`.

> `lan` mode listens on all network interfaces over plain HTTP and is only appropriate
> on a trusted network.

## Data location

By default the app uses `~/doopass/appdata` (the same location as the original Python
implementation), so an existing `store.enc` and `settings.json` are picked up automatically.
Override the root with the `DOOPASS_HOME` environment variable.

```
~/doopass/
└── appdata/
    ├── store.enc        # encrypted store
    ├── settings.json    # application settings
    ├── backups/         # dated store backups
    └── doopass.log      # log file
```

Application events (start/stop, store and backup operations, settings changes, errors)
are appended to `doopass.log` and it rotates at 5 MB. Secrets are never written to the log.

## Store format

The on-disk file is:

```
base64url( salt[16] || iterations[4, big-endian] || fernet_token )
```

where `fernet_token = 0x80 || timestamp[8] || IV[16] || AES-128-CBC(ciphertext) || HMAC-SHA256(32)`.
The plaintext is a newline-separated list of `key = value` pairs.

## License

See the LICENSE file in the original repository. This rewrite follows the same license.
