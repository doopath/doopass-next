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

Hotkeys: `Q`/`Esc` quit, `H`/`F1` help, `Tab`/`Shift+Tab` next/previous,
`J`/`K`/arrows focus next/previous.

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

## Portable single-file build

```powershell
scripts\publish-portable            # or: powershell -File scripts\publish-portable.ps1
```

Produces one self-contained executable per platform (no .NET install required):

* `dist/portable/win-x64/Doopass.exe`
* `dist/portable/linux-x64/Doopass`

Run it directly:

```bash
dist\portable\win-x64\Doopass.exe serve --port 7240     # Windows
chmod +x dist/portable/linux-x64/Doopass                # Linux
./dist/portable/linux-x64/Doopass serve                 # Linux
```

## Autostart on Windows (web server at sign-in)

```powershell
scripts\install-autostart -Bind localhost     # or -Bind lan
scripts\uninstall-autostart
```

`install-autostart` copies `Doopass.exe` to `%LOCALAPPDATA%\Doopass`, creates a hidden
launcher (VBS) and adds it to the Startup folder, and places a shortcut that opens the
web interface.

Options:

* `-Port 7240`
* `-Bind localhost|lan` (default `localhost`)
* `-DryRun` — preview what would be done without changing anything
* `-NoDesktopShortcut` — skip the desktop shortcut

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

## Store format

The on-disk file is:

```
base64url( salt[16] || iterations[4, big-endian] || fernet_token )
```

where `fernet_token = 0x80 || timestamp[8] || IV[16] || AES-128-CBC(ciphertext) || HMAC-SHA256(32)`.
The plaintext is a newline-separated list of `key = value` pairs.

## License

See the LICENSE file in the original repository. This rewrite follows the same license.
