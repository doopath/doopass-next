# Feature: PWA (installable web app)

The web UI is a Progressive Web App. Installed, it runs in its own window with
its own icon, and it has a minimal offline shell. All PWA assets are **embedded
resources** served from virtual routes, so a single-file binary can be installed
with no extra files on disk.

## Routes served by the host

| Route | Source | Cache-Control |
| ----- | ------ | ------------- |
| `/pwa/manifest.webmanifest` | embedded `wwwroot/pwa/manifest.webmanifest` | `no-cache` |
| `/pwa/sw.js` | embedded `wwwroot/pwa/sw.js`, plus `Service-Worker-Allowed: /` | `no-cache` |
| `/pwa/icon-192.png`, `/pwa/icon-512.png` | generated into `.pwa-assets/`, embedded | 1 h |
| `/pwa/icon-maskable-192.png`, `/pwa/icon-maskable-512.png` | generated | 1 h |
| `/pwa/apple-touch-icon.png` | generated | 1 h |
| `/pwa/favicon.svg` | `assets/logo.svg` | 1 h |
| `/pwa/favicon.ico`, `/favicon.ico` | generated | 1 h |

## Manifest

```json
{
  "id": "/",
  "name": "Doopass — Password Manager",
  "short_name": "Doopass",
  "start_url": "/?source=pwa",
  "scope": "/",
  "display": "standalone",
  "display_override": ["fullscreen", "standalone"],
  "background_color": "#0d0f15",
  "theme_color": "#4f46e5",
  "icons": [ ... 192/512, plus maskable variants ... ]
}
```

`App.razor` links the manifest, the Apple touch icon, the SVG + `.ico` favicons
and the Apple/Chrome mobile-web-app meta tags. `theme.js` keeps the
`theme-color` meta in sync with the chosen light/dark theme.

## Service worker

`sw.js` (cache name `doopass-pwa-v2`):

* precaches `/`, `/app.css`, `/theme.js`, `/app.js`,
  `/_framework/blazor.web.js`, the manifest and the icons; a failed precache is
  tolerated (`Promise.allSettled`) so one flaky asset cannot break installation;
* `skipWaiting()` on install, deletes old caches and `clients.claim()` on
  activate — **bump `CACHE` when served assets change**;
* fetch handling: same-origin GET only; `/_blazor*` (SignalR) is never cached;
  navigations go network-first with a cached shell fallback (503 "Offline" text
  when nothing is cached); other assets are cache-first with fill-on-miss.

Registered by `app.js` with `{ scope: '/' }`; registration failures are silent,
and on a non-secure origin the worker simply does not register.

Offline behaviour is honest: the shell loads, but every vault operation needs
the server and the Blazor circuit reconnects when the host returns.

## Installation matrix

| Platform | How |
| -------- | --- |
| Chrome / Edge desktop | install icon in the address bar, or Settings → "Install Doopass…" |
| Android Chrome / Samsung Internet | install prompt / "Add to Home screen" → real WebAPK |
| iOS / iPadOS Safari 16.4+ | Share → "Add to Home Screen" |
| Safari 17 (macOS) | File → "Add to Dock" |
| Firefox desktop | not supported (home-screen shortcut on mobile only) |

The Settings page shows a card driven by `doopass.pwa.probe`, which returns one
of:

| Result | UI |
| ------ | -- |
| `standalone` | "installed and running in its own window" |
| `installable` | install button, uses the captured `beforeinstallprompt` |
| `ios` | instructions for Share → Add to Home Screen |
| `manual` | browser-menu instructions |
| `no-service-worker` | "installation requires a secure origin" |

`promptInstall` returns `accepted`, `dismissed`, `unavailable` or `error`, which
the page turns into a success/info toast.

## Secure origin requirement

Installation requires `http://localhost` or HTTPS with a certificate the browser
trusts. Consequences:

* a plain-HTTP LAN address (`--lan` without `--tls`) can use the UI but cannot
  install the app;
* a self-signed certificate is fine for the Android client (pinning) but is not
  trusted by browsers, so PWA installation is blocked;
* a public CA certificate (or a locally trusted one) makes installation work.

## Icons and regeneration

```bash
python scripts/generate-icons.py
```

Resizes `assets/logo256.png` into every size the app needs under `.pwa-assets/`
(ignored by git, embedded by the csprojs), refreshes the Android
`Resources/AppIcon/appicon.png`, writes `app.ico` for the Windows executable, and
copies `assets/logo.svg` into `Doopass.WebUi/wwwroot/brand.svg` for the sidebar
and auth card.

## PWA vs the Android app

| | PWA | Android client |
| --- | --- | --- |
| Runs | web UI in a browser window | native thin client + WebView |
| Needs | Blazor Server connection | REST + SignalR |
| Host discovery | no (typing the URL) | yes (UDP) |
| Certificate pinning | no (browser rules) | yes |
| Offline | shell only | no offline vault either |
| Install over plain HTTP LAN | no | n/a (APK) |

Installing both does not conflict.

## Where the code lives

* `src/Doopass.Cli/wwwroot/pwa/` — manifest, worker
* `src/Doopass.Cli/Api/WebHostFactory.cs` — routes and headers
* `src/Doopass.Cli/Doopass.Cli.csproj` — embedded resources, icon generation
* `src/Doopass.WebUi/wwwroot/app.js` — registration, install prompt, probe
* `src/Doopass.WebUi/Components/Pages/Settings.razor` — install card
* `scripts/generate-icons.py` — asset generation
