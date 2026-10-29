# Development

## Requirements

* .NET SDK **10.0**
* For the phone: `maui-android` workload, Android SDK (`platforms;android-36`,
  `build-tools;36.0.0`), JDK 17+ (JDK 21 recommended), `ANDROID_HOME`, `JAVA_HOME`
* Optional: Python 3 for `scripts/generate-icons.py`

## Build and run

```bash
dotnet build Doopass.slnx
dotnet test  Doopass.slnx

dotnet run --project src/Doopass.Cli                 # TUI
dotnet run --project src/Doopass.Cli -- serve        # web UI + API on :7240
dotnet run --project src/Doopass.Cli -- serve --lan --tls ~/doopass/doopass.pfx --tls-pass CHANGE-ME
```

The Android app is built separately (`scripts/build-android.sh`); it is not part
of `Doopass.slnx` so the solution builds without the MAUI workload.

To experiment without touching your real vault:

```bash
DOOPASS_HOME=/tmp/doopass-dev dotnet run --project src/Doopass.Cli -- serve
```

## Repository layout

```
Directory.Build.props      shared TFM/nullable/lang settings
Doopass.slnx               solution (src + tests, no mobile)
assets/                    logo masters used by the icon generator
.pwa-assets/               generated icons (git-ignored, embedded by csprojs)
scripts/                   publish, package, autostart, icons, smoke tests
src/Doopass.Core/          storage, crypto, settings, logging
src/Doopass.Tui/           Terminal.Gui front-end
src/Doopass.WebUi/         shared Razor components + assets
src/Doopass.Cli/           entry point, web host, REST, hub, PWA assets
src/Doopass.Mobile/        Android thin client
tests/                     Core, Tui, Api xUnit projects
docs/wiki/                 this documentation (published to the GitHub wiki)
```

## Architectural rules

1. **`Core` stays platform-free.** No UI, HTTP, MAUI or ASP.NET Core types. It
   must remain usable by all three front-ends and by tests.
2. **Screens talk to contracts.** Web components use `IVaultSession` /
   `IVaultEvents` / `ISecretClipboard`; they never see `Store`, `Settings` or
   `HttpClient`. Anything else breaks the phone.
3. **Format compatibility is non-negotiable.** `store.enc`, `settings.json`,
   backup names and the API wire shapes are contracts; changing them is a
   breaking change and needs a migration story.
4. **One writer per vault.** Do not add a second in-process `Store` that
   persists independently without addressing the last-write-wins problem.
5. **Assets are embedded.** New web assets go into a csproj as
   `EmbeddedResource` with a logical name and get a mapped route with an explicit
   `Cache-Control`.
6. **Secrets never reach logs or error messages.** Log key names, counts and
   paths only.

## Adding a feature — checklist

1. **Domain rule** → `Doopass.Core` (with a unit test).
2. **API** (if a thin client needs it) → `ApiVaultCoordinator` +
   `VaultEndpoints` + `RemoteVaultSession` + an API test.
3. **Web UI** → component in `Doopass.WebUi`, subscribing to `IVaultEvents` and
   unsubscribing in `Dispose`.
4. **TUI** → screen or shortcut; update `TuiKeyMap`/`RouteKey` if a global key is
   involved and cover it in `TuiKeyMapTests`.
5. **Docs** → update the relevant wiki page (`docs/wiki/`) and the README table
   if user-facing.
6. **Version/PWA** → bump `sw.js` `CACHE` only when assets change; bump versions
   only for a release.

## Generating API documentation

The compiler emits XML documentation for every public member:

```bash
dotnet build Doopass.slnx        # writes src/*/bin/Debug/net10.0/*.xml
python scripts/generate-api-docs.py
```

* `Directory.Build.props` sets `GenerateDocumentationFile=true` for all
  projects, so a missing `///` on a public type produces CS1591 — the build is
  expected to stay warning-free, which keeps the public API documented.
* `scripts/generate-api-docs.py` reads the generated XML files and writes
  `docs/wiki/API-Reference-<Assembly>.md` (types grouped by namespace, members
  with parameters, returns and remarks). Re-run it whenever public members
  change, then commit both the code and the regenerated pages.

## Code conventions

| Area | Convention |
| ---- | ---------- |
| Language | C# latest, nullable enabled, implicit usings |
| Async | `ValueTask` for hot-path session APIs; no `.Result`/`.Wait()`; `ConfigureAwait` not used (application code) |
| Exceptions | typed Core exceptions for domain failures; never swallow except in logging |
| Logging | `AppLogger.Info/Debug/Error`, English messages, no secrets |
| Comments | explain **why**, not what; English |
| UI copy | English, short, lowercase-heavy TUI labels |
| Files | UTF-8, LF/CRLF per `.gitattributes`, no BOM |
| Tests | xUnit, behaviour-named methods, one assertion theme per test |
| Commits | `type(scope): summary` — e.g. `fix(api): …`, `docs(wiki): …`, `test: …` |

`Directory.Build.props` is the only place to change the target framework,
nullability or documentation settings for the whole solution.

## Adding an icon or asset

1. Drop the master into `assets/`.
2. Run `python scripts/generate-icons.py` (writes `.pwa-assets/`, the Android
   icon and the RCL brand SVG).
3. Reference the generated file from a csproj as an embedded resource.
4. Add the route in `WebHostFactory` with the right content type and cache
   policy.

## Debugging

| Need | Approach |
| ---- | -------- |
| See what the app did | `tail -f ~/doopass/appdata/doopass.log` |
| Isolate state | `DOOPASS_HOME=/tmp/x` for every command |
| API without a phone | `scripts/smoke-api.ps1` or `curl` (see [API Reference](API-Reference.md)) |
| Phone logs | `adb logcat | grep -i doopass`; unhandled exceptions are also written to the app log |
| Stale host on the phone | check `ss -ltnp | grep doopass` on the PC |
| UI issue in the browser | DevTools; `theme.js`/`app.js` are plain scripts with no build step |

## Publishing the wiki

`scripts/publish-wiki.sh` mirrors `docs/wiki/` into the repository's GitHub wiki:

```bash
scripts/publish-wiki.sh            # clone/update and push
scripts/publish-wiki.sh --dry-run  # show what would change
```

It clones `https://github.com/doopath/doopass-next.wiki.git`, copies `*.md`
(including `_Sidebar.md` and `_Footer.md`), commits and pushes. GitHub wikis are
a separate git repository; the sources stay reviewable in `docs/wiki/`.
