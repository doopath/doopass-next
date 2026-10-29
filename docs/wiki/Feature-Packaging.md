# Feature: Packaging and Deployment

How Doopass is built into something a user can run.

## 1. Portable single-file build

```powershell
scripts\publish-portable                      # or: powershell -File scripts\publish-portable.ps1
```

```bash
scripts/publish-portable.sh                   # both platforms
scripts/publish-portable.sh -r linux-x64     # one target
scripts/publish-portable.sh -c Debug -r linux-x64,win-x64 -o /tmp/portable
```

Defaults: `Release`, `win-x64` + `linux-x64`, output `dist/portable`.

Outputs:

```
dist/portable/win-x64/doopass.exe
dist/portable/linux-x64/doopass
```

* `PublishSingleFile` + `SelfContained`: no .NET runtime needed on the target.
* Web assets are embedded, so no `wwwroot` folder is published (the csproj
  removes `wwwroot/**` from content).
* `.pdb` files are deleted after publishing, including stale ones copied from
  `bin/` by an incremental build.
* The shell script marks the Linux binary executable and can cross-publish from
  either OS.
* If the SDK has no ASP.NET Core targeting pack, publishing fails with
  `NETSDK1226`; the script prints the package names for Arch/Debian/Fedora and
  suggests `AllowMissingPrunePackageData=true` when no root access is available.

Requirements: .NET SDK 10. Distro SDKs ship ASP.NET Core separately
(`aspnet-runtime` + `aspnet-targeting-pack` on Arch/Omarchy,
`aspnetcore-runtime-10.0` + `aspnetcore-targeting-pack-10.0` on Debian/Ubuntu).

## 2. Release archives

```powershell
scripts\package-release
```

Produces, from an existing portable publish:

```
dist/release/doopass-5.0.0-win-x64.zip
dist/release/doopass-5.0.0-linux-x64.tar.gz
```

Each archive contains the executable, the install/uninstall scripts, the README
and — on Linux — the systemd unit. Ready to attach to a GitHub Release.

## 3. Autostart at sign-in

Contract: the executable path is the first argument; the scripts copy it to
`~/.local/bin` (`%USERPROFILE%\.local\bin` on Windows).

**Windows**

```powershell
powershell -File scripts\install-autostart-win.ps1 .\doopass.exe          # localhost
powershell -File scripts\install-autostart-win.ps1 .\doopass.exe lan      # LAN
powershell -File scripts\uninstall-autostart-win.ps1
```

Options: `-Port 7240`, `-DryRun`, `-NoDesktopShortcut`. Creates a startup
shortcut and adds firewall rules for the API port and UDP 7241.

**Linux**

```bash
scripts/install-autostart-linux.sh ./doopass                 # localhost
scripts/install-autostart-linux.sh ./doopass lan [--linger]  # LAN
scripts/uninstall-autostart-linux.sh
```

Installs a systemd **user** service at
`~/.config/systemd/user/doopass.service` and enables it with `enable --now`.
`--linger` keeps it running without an active login session. The unit runs
`doopass serve` (with `--lan` when requested).

> LAN mode over plain HTTP listens on all interfaces and is only appropriate on
> a trusted network.

## 4. Android APK / AAB

Requirements: .NET SDK 10 with the `maui-android` workload, Android SDK
(`platforms;android-36`, `build-tools;36.0.0`), JDK 17+ (JDK 21 for the .NET 10
Android pack), `ANDROID_HOME`/`ANDROID_SDK_ROOT` and `JAVA_HOME` set.

```bash
scripts/build-android.sh                      # Debug APK, directly installable
```
```powershell
scripts\build-android.ps1
```
```bash
dotnet publish src/Doopass.Mobile -c Release -f net10.0-android -p:AndroidPackageFormat=apk
dotnet publish src/Doopass.Mobile -c Release -f net10.0-android -p:AndroidPackageFormat=aab
```

Artifacts:

* `src/Doopass.Mobile/bin/Debug/net10.0-android/io.github.doopath.doopass-Signed.apk`
* `src/Doopass.Mobile/bin/Release/net10.0-android/io.github.doopath.doopass-Signed.apk`

Release builds keep AOT/trimming **off** for readable diagnostics; enabling them
requires a `TrimmerRootDescriptor.xml` for System.Text.Json/SignalR reflection.
Debug builds embed assemblies so `adb install` works without IDE staging.

`Doopass.Mobile` is intentionally absent from `Doopass.slnx`: a plain
`dotnet build Doopass.slnx` must work without the MAUI workload installed.

## 5. Icons and branding

```bash
python scripts/generate-icons.py
```

Regenerates `.pwa-assets/` (PWA icons, favicons, `app.ico` for the executable),
the Android launcher icon and the RCL brand SVG. `.pwa-assets/` is git-ignored,
so a fresh clone must run the script (or restore a build cache) before
publishing — the csprojs embed files from that directory.

## 6. Version bumps

Version `5.0.0` appears in several places; keep them in sync:

1. `src/Doopass.Core/Constants.cs` → `Version`;
2. `Directory.Build.props` is version-free, but every `.csproj` sets `<Version>`;
3. `src/Doopass.Core/Configuration/DefaultConfig.json` → `local.version`;
4. `src/Doopass.Mobile/Doopass.Mobile.csproj` →
   `ApplicationDisplayVersion`, `ApplicationVersion`;
5. `src/Doopass.Cli/wwwroot/pwa/sw.js` → bump `CACHE` when assets change;
6. `README.md` and this wiki if behaviour changed.

`/api/vault/state` reports the constant, so a client can detect a version skew.

## 7. What is *not* packaged

* No MSI/deb/rpm/AppImage, no NuGet package, no container image.
* No code signing for the Windows executable.
* No CI pipeline in the repository (`.github/` is absent); builds and tests are
  run locally with `dotnet build Doopass.slnx` / `dotnet test Doopass.slnx`.
