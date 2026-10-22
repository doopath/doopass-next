# Doopass (.NET 10)

A cross-platform rewrite of the **Doopass** password manager in **.NET 10**, with:

* a **TUI** front-end (Terminal.Gui v2) that mirrors the original Textual interface;
* a **Fluent Design web** front-end (ASP.NET Core + Fluent UI Blazor) that runs as a local server.

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

The default port is `7240`; the browser opens automatically (use `--no-browser` to disable).
Open <http://localhost:7240> and log in or initialize a store.

> When running from source (non-published), the app uses the ASP.NET Core `Development`
> environment so framework static assets (`_framework/blazor.web.js`) load correctly.
> Published builds run in the configured environment.

For a production build, publish first:

```bash
dotnet publish src/Doopass.Cli -c Release -o out
./out/Doopass serve --port 7240
```

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
