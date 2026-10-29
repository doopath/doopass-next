<div align="center">

  <img src="assets/logo256.png" alt="Doopass logo" width="104" />

  # Doopass

  **A cross-platform password manager for the terminal, the browser and phones — rewritten in .NET 10.**

  [![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
  [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20Android-blue)](#run-it)
  [![Frontends](https://img.shields.io/badge/frontends-TUI%20%2B%20Web%20(PWA)%20%2B%20Android%20thin%20client-7c3aed)](#run-it)
  [![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

</div>

Doopass keeps everything in a single encrypted `store.enc` file and offers
three front-ends for it:

* a **TUI** built on Terminal.Gui v2 that mirrors the original Textual interface;
* a **custom-styled web UI** (ASP.NET Core Blazor Server) with light and dark
  themes, served as a local web app;
* an **Android thin client** (MAUI + Blazor) that connects to the same host
  over a private REST API — the phone stores nothing but the host address,
  its certificate pin and the session token.

This project is a from-scratch .NET 10 rewrite of the original
[Doopass](https://github.com/doopath/PasswordManager/) (Python + Textual). The
encrypted store format is **fully compatible**, so existing files open without
any migration — just point the new app at the same `~/doopass` folder.

## Features

- **Drop-in compatible** with the original Python implementation: Fernet crypto
  (AES-128-CBC + HMAC-SHA256) derived with PBKDF2-HMAC-SHA256 (100 000 iterations).
- **Three front-ends, one store** — the same vault is available in the terminal,
  in the browser and on an Android phone, with dated backups, restore and
  settings everywhere.
- **Thin-client architecture** — the host is the single unlocked vault; every
  client (browser session or API token) shares the encrypted file safely.
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
| `--tls <pfx> [--tls-pass P]`  | serve HTTPS from a PKCS#12 certificate (binds    |
|                               | every interface); the startup log prints the     |
|                               | SHA-256 fingerprint, which thin clients pin for  |
|                               | trust-on-first-use                               |
| `--announce`                  | force LAN discovery announce (also on with `--lan`) |

The UI and all assets are embedded in the executable, so nothing else is needed
to serve the app. Open <http://localhost:7240> and log in or initialize a store.

### REST API and thin clients

`serve` mode also exposes a private REST API for thin clients (the Doopass
Android app) and a SignalR channel for change push, both on the same port:

* `GET /api/vault/state` — anonymous: `{storeExists, unlocked, serverVersion, certFingerprint}`.
* Everything else is `Authorization: Bearer <token>`; tokens come from
  `POST /api/auth/login` / `POST /api/auth/init` and die on logout or a sliding
  30 min TTL (configurable via the optional `api.session_ttl_minutes` key in
  `settings.json`).
* Vault pairs, backups and settings live under `/api/vault/*`, `/api/backups*`
  and `/api/settings*` — full table in `THIN_CLIENT_PROMPT.md`.
* Changes made by any client raise `vaultChanged` / `backupsChanged` /
  `settingsChanged` on the `/hubs/vault` SignalR channel, so every connected
  device updates without reloads.
* `secret` responses carry `Cache-Control: no-store`; errors are always
  `{ "error": "<message>" }` with 400/401/404/409 semantics; login/init are
  rate-limited to 5 requests per minute per IP (429 after that).

### LAN discovery

With `--lan` (or `--announce`), the host answers UDP broadcasts on port 7241:
send the ASCII datagram `DOOPASS_DISCOVER`, get back the JSON announcement with
the host name, web port, TLS flag and certificate fingerprint. Phones use this
to find the PC without typing an address — allow **inbound UDP 7241** in your
firewall (the autostart installers do it on Windows / ufw).

When the API is exposed, the host also relays and keeps its vault consistent:
after a backup restore the coordinator re-reads the store automatically, and
`vaultLocked` is pushed to clients whose session ended (logout elsewhere, TTL,
`G`-lock in the TUI happens on TTL as well).

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
> host whose certificate the browser trusts. A plain-HTTP LAN address (`--lan`
> without TLS) can use the UI but cannot install the app, and a self-signed
> certificate — fine for the Android client, see
> [Self-signed HTTPS with certificate pinning](#self-signed-https-with-certificate-pinning)
> — is not trusted by browsers, which blocks the install.

To regenerate the icons after a design change, drop the new artwork into
`assets/` and run:

```bash
python scripts/generate-icons.py    # PWA icons + favicons + Android icon + exe icon + brand mark
```

It resizes the PNG master into every size the app needs (writing the generated
files to `.pwa-assets/`, which the csprojs embed), refreshes
`src/Doopass.Mobile/Resources/AppIcon/appicon.png`, and copies the vector master
`assets/logo.svg` into the RCL as the sidebar / login brand mark
(`src/Doopass.WebUi/wwwroot/brand.svg`, served as the SVG favicon too).

#### PWA vs the Android thin client

The PWA runs the **web** UI in a browser window — it needs the Blazor Server
connection. The Android app is a **native** thin client with its own icon and
its own discovery flow; installing one does not conflict with the other. If you
manage secrets mainly from a phone, the Android app is the stronger choice: it
survives browser sessions, pins the host certificate and warns clearly before
using an unencrypted connection.

## Android thin client

The phone holds **no vault** — it stores only the host address, the pinned
certificate fingerprint and the session token (in SecureStorage / Android
Keystore). Everything else lives on the PC host.

### Build and install

Requirements: .NET SDK 10 with the `maui-android` workload, an Android SDK
(`platforms;android-36`, `build-tools;36.0.0`) and a JDK 17+ (JDK 21 is what
the .NET 10 Android pack expects), with `ANDROID_HOME` (or `ANDROID_SDK_ROOT`)
and `JAVA_HOME` pointing at them.

```powershell
scripts\build-android.ps1                       # Windows: Debug APK (installable directly)
```

```bash
scripts/build-android.sh                        # Linux/macOS: Debug APK (installable directly)
```

```bash
dotnet publish src/Doopass.Mobile -c Release -f net10.0-android -p:AndroidPackageFormat=apk
dotnet publish src/Doopass.Mobile -c Release -f net10.0-android -p:AndroidPackageFormat=aab   # Play-format bundle
```

Artifacts:

* `src/Doopass.Mobile/bin/Debug/net10.0-android/io.github.doopath.doopass-Signed.apk`
* `src/Doopass.Mobile/bin/Release/net10.0-android/io.github.doopath.doopass-Signed.apk`

> [!NOTE]
> The first release builds run with AOT/trimming disabled (full IL) for stable
> diagnostics; enabling them needs a `TrimmerRootDescriptor.xml` pass for the
> System.Text.Json and SignalR reflection surfaces. Debug APKs embed their
> assemblies too (Fast Deployment is off), so they are directly installable
> with `adb install` — no IDE staging required.

### Connecting

1. Start the host on the PC: `doopass serve --lan` (plain HTTP + discovery).
   Prefer `--tls mycert.pfx --tls-pass …` — the app then pins the SHA-256
   fingerprint printed in the host log and warns loudly when it is absent.
   A self-signed certificate is enough; see
   [Self-signed HTTPS with certificate pinning](#self-signed-https-with-certificate-pinning).
2. On the phone: the **Connect** screen scans the LAN over UDP as soon as it
   opens — pick a discovered host, or switch to *Manual* and type the full
   address **including the scheme**: `https://192.168.1.10:7240` for a TLS
   host. Without a scheme the app assumes `http://`, which a TLS-only host
   answers with silence. *Search again* re-runs the scan.
3. Confirm the fingerprint ("Trust and connect"), then log in with the master
   password. Everything afterwards mirrors the web version, pushed live:
   pairs, copy, backups (incl. safety backups on restore), settings, themes.

The login card always shows the current host with a **Change host** action
that returns to the picker, whether or not that host still answers — so a
moved host does not require reinstalling or clearing app data. If the saved
address stops answering (for example the PC got a new IP), the app says so
above the password field after a short probe and leaves the session token in
place; pick the host again, or go back to the login screen without changing
anything.

Features and limitations:

* the app reads `vaultChanged` / `backupsChanged` / `settingsChanged` /
  `vaultLocked` events from the `/hubs/vault` SignalR channel and stays in
  sync with the web UI and other phones;
* a 30 min sliding session TTL relocks the vault; expired tokens land on the
  login screen automatically;
* a stale host address is detected in about four seconds instead of waiting
  for a long request timeout, and the login screen offers the host picker;
* discovery probes both `255.255.255.255` and each interface's directed
  broadcast (e.g. `192.168.31.255`), which survives Wi-Fi stacks that drop the
  global broadcast;
* the emulator cannot reach LAN broadcasts — type `http://10.0.2.2:7240`
  manually there;
* plain HTTP shows a permanent warning; IPSec rules of the router or at least
  `--tls` are recommended on shared networks.

#### Self-signed HTTPS with certificate pinning

The Android client pins the host certificate, so no public CA, domain or
reverse proxy is required: a self-signed certificate works as long as its
fingerprint is confirmed once on the phone. The pin lives in Android Keystore,
and every later handshake is compared against it.

Browsers do not pin, so this certificate only gives them a warning — and the
PWA cannot be installed over an untrusted one. This setup is for the Android
client; see [Install as an app](#install-as-an-app-pwa) for the browser
requirements.

1. Note the host's LAN address — this is what the phone will dial:

   ```bash
   ip -4 -o addr show scope global | awk '{print $4}' | cut -d/ -f1   # Linux
   ipconfig getifaddr en0                                             # macOS
   ```

2. Create the certificate with that address in its SAN. Keep the files outside
   the repository — `~/doopass/` next to the store is the natural place. This
   needs OpenSSL 1.1.1+ (on macOS use the Homebrew `openssl` if the system
   LibreSSL rejects `-addext`):

   ```bash
   cd ~/doopass
   LAN_IP=192.168.1.10            # from step 1
   HOST=$(hostname)
   openssl req -x509 -newkey rsa:2048 -sha256 -days 825 -nodes \
     -keyout doopass.key -out doopass.crt \
     -subj "/CN=$HOST" \
     -addext "subjectAltName=DNS:$HOST,DNS=localhost,IP:$LAN_IP,IP:127.0.0.1"
   openssl pkcs12 -export -out doopass.pfx \
     -inkey doopass.key -in doopass.crt -passout pass:CHANGE-ME
   ```

   The same commands run in Git Bash on Windows; PowerShell can do it too:

   ```powershell
   $cert = New-SelfSignedCertificate -Subject 'CN=doopass' `
       -TextExtension @('2.5.29.17={text}DNS=doopass&DNS=localhost&IPAddress=192.168.1.10&IPAddress=127.0.0.1') `
       -NotAfter (Get-Date).AddDays(825)
   Export-PfxCertificate -Cert $cert -FilePath doopass.pfx `
       -Password (ConvertTo-SecureString 'CHANGE-ME' -AsPlainText -Force)
   ```

3. Start the host in TLS mode. `--tls` already binds every interface, so
   `--lan` only adds the discovery announce:

   ```bash
   doopass serve --lan --tls ~/doopass/doopass.pfx --tls-pass CHANGE-ME
   ```

   The startup log prints the fingerprint in the compact form the app pins:

   ```
   TLS certificate SHA-256 fingerprint: d3894b6e…c8e50f
   ```

   To keep the password out of the shell history, use `tls_pfx` and `tls_pass`
   under `[api]` in `settings.json` instead (the password is stored there in
   plain text).

4. Open the firewall once — inbound **TCP 7240** for the app and **UDP 7241**
   for discovery, see [Discovery finds nothing?](#discovery-finds-nothing).

5. On the phone, **Connect** → the discovered host carries an *Encrypted · TLS*
   tag and shows the fingerprint. Over *Manual*, include the scheme
   (`https://192.168.1.10:7240`); discovery fills it in itself. Compare the
   fingerprint with the host log before tapping **Trust and connect**, then log
   in with the master password. From then on a different certificate on that
   address fails the handshake instead of leaking the password. For an
   independent check on any machine:

   ```bash
   openssl x509 -in doopass.crt -noout -fingerprint -sha256
   # drop the colons and lowercase the hex to compare with the app
   ```

6. To rotate the certificate, regenerate it and pick the host again through
   **Change host**: the app shows the new fingerprint and re-pins it on
   confirmation. Rotating without re-pairing is the intended failure mode — the
   app refuses the host rather than trusting it silently.

Notes:

* the app compares the fingerprint only, not the host name or the SAN, so the
  same certificate keeps working when DHCP gives the PC a new address;
  browsers would complain about the mismatch;
* the trust-on-first-use probe only reads the fingerprint — no vault data is
  sent before you confirm it, and the pin is stored after you tap
  **Trust and connect**;
* the flow above is verified with the Android client: the pin survives app
  restarts, and a swapped certificate is rejected on the first request, before
  any vault data is read;
* treat `doopass.key` as a secret, and never commit `doopass.pfx` or the key.

#### Discovery finds nothing?

The phone and the PC must be on the same Wi-Fi/LAN, and the PC firewall must
allow both the discovery probe and the API port. With `ufw` (the default on
Omarchy/Arch desktops):

```bash
sudo ufw allow 7241/udp comment 'Doopass discovery'
sudo ufw allow 7240/tcp comment 'Doopass web/API'
# if the host was started with --port 7241, allow that TCP port instead:
sudo ufw allow 7241/tcp comment 'Doopass web/API (custom port)'
```

Every custom `--port` needs its own inbound TCP rule. An unlisted port is the
confusing case: the host answers when reached from the PC itself, while the
phone just times out — and because `7240`/`7241` keep working, only that one
address looks broken.

USB debugging alone does not carry UDP broadcasts. For a cable-only setup,
forward the API port to the phone's loopback and type that address into
*Manual* — prefixed with `https://` when the host runs with `--tls`:

```bash
adb reverse tcp:7240 tcp:7240
# in the app's Manual tab: 127.0.0.1:7240
# for a TLS host:          https://127.0.0.1:7240
```

Make sure only one host process is running on the PC: a stale `doopass serve`
on the saved port can answer the app's probe with an old build that has no
REST API, which looks exactly like "the host is unreachable". Check the
listening sockets with `ss -ltnp | grep doopass`.

> [!NOTE]
> On some Xiaomi/Redmi HyperOS builds, `adb install` needs *Developer options →
> Install via USB* (and sometimes *USB debugging (Security settings)*) to be
> enabled; without it the installer reports
> `INSTALL_FAILED_USER_RESTRICTED: Install canceled by user`.

## Deploy it

### Portable single-file build

```powershell
scripts\publish-portable            # or: powershell -File scripts\publish-portable.ps1
```

```bash
scripts/publish-portable.sh                    # both platforms
scripts/publish-portable.sh -r linux-x64       # a single target
scripts/publish-portable.sh -c Debug -o /tmp/doopass
```

The two scripts are equivalent and can cross-publish from either OS: the
PowerShell one takes `-Configuration`, `-RuntimeIdentifiers` and `-OutputRoot`,
the shell one takes `-c`, `-r` (comma-separated or repeated) and `-o`. Defaults
are `Release`, `win-x64` + `linux-x64` and `dist/portable`; the shell script also
marks the Linux binary executable.

Produces one self-contained executable per platform (no .NET install needed):

* `dist/portable/win-x64/doopass.exe`
* `dist/portable/linux-x64/doopass`

Only the executable ends up in the output folder: `.pdb` symbol files are
removed after publishing, including stale ones that an incremental build may
have copied from `bin/`. Ship the binary as is — symbols are debugger-only and
are not needed at runtime.

> [!NOTE]
> Distro SDK packages ship ASP.NET Core as a separate package. If publishing
> fails with `NETSDK1226: Prune Package data not found … Microsoft.AspNetCore.App`,
> install the matching ASP.NET Core pack — `aspnet-runtime` + `aspnet-targeting-pack`
> on Arch/Omarchy, `aspnetcore-runtime-10.0` + `aspnetcore-targeting-pack-10.0`
> on Debian/Ubuntu, the same names via `dnf` on Fedora. The script prints this
> hint on its own when it detects that error.

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

## Documentation

The full documentation lives in the **GitHub wiki**:
<https://github.com/doopath/doopass-next/wiki> — product overview, architecture,
business rules, one page per application, one page per feature, configuration,
API reference and a generated code reference.

The wiki sources are versioned with the code in [`docs/wiki/`](docs/wiki):

| Page | Contents |
| ---- | -------- |
| [Overview](docs/wiki/Overview.md) | what Doopass is and what it guarantees |
| [Architecture](docs/wiki/Architecture.md) | components, process model, data flow |
| [Business Rules](docs/wiki/Business-Rules.md) | every rule the product enforces |
| [Data Formats](docs/wiki/Data-Formats.md) | `store.enc`, `settings.json`, wire formats |
| [Security Model](docs/wiki/Security-Model.md) | crypto, threat model, trust boundaries |
| [Applications](docs/wiki/Application-Core.md) | Core, CLI host, web UI, REST API, TUI, Android |
| [Features](docs/wiki/Feature-Store.md) | store, backups, settings, sessions, sync, discovery, TLS, PWA, logging, packaging |
| [Reference](docs/wiki/Configuration.md) | configuration, API, testing, development, operations, limitations |
| [Glossary](docs/wiki/Glossary.md) | project vocabulary |

Publish or refresh the wiki (needs `gh auth login` and one page created in the
web UI first):

```bash
scripts/publish-wiki.sh --dry-run   # preview
scripts/publish-wiki.sh             # mirror docs/wiki/ into the wiki repo
```

API documentation is generated from the XML comments the compiler emits
(`GenerateDocumentationFile=true` in `Directory.Build.props`):

```bash
dotnet build Doopass.slnx
dotnet build src/Doopass.Mobile/Doopass.Mobile.csproj   # not in the solution
python scripts/generate-api-docs.py
```

That rewrites `docs/wiki/API-Reference-*.md`; commit them together with the code
change.

## Project layout

| Project                      | Description                                                     |
| ---------------------------- | --------------------------------------------------------------- |
| `src/Doopass.Core`           | Store, Fernet/PBKDF2 crypto, settings, backups, validation, logs |
| `src/Doopass.Tui`            | Terminal.Gui v2 interface (mirrors the original TUI)             |
| `src/Doopass.WebUi`          | Shared Razor Class Library: components, themes, IVaultSession    |
| `src/Doopass.Cli`            | Single entry point: TUI or web host (Blazor + REST API + hub)    |
| `src/Doopass.Mobile`         | Android thin client (MAUI + Blazor WebView)                      |
| `tests/Doopass.Core.Tests`   | xUnit tests: format compatibility, CRUD, backups, settings       |
| `tests/Doopass.Tui.Tests`    | xUnit tests: key mapping and screen behaviour                    |
| `tests/Doopass.Api.Tests`    | xUnit integration tests: the REST API on a real host             |

---

Doopass is a rewrite of the original [Doopass](https://github.com/doopath/PasswordManager/)
and follows the same license (GPL-3.0 — see the [LICENSE](LICENSE) file).
