# Application: CLI Host (`Doopass.Cli`)

The single executable. It is a `Microsoft.NET.Sdk.Web` project with
`AssemblyName=doopass`, and it contains both front-ends' entry points plus the
web host.

```
src/Doopass.Cli
├── Program.cs                  mode dispatch + argument parsing
├── Api/
│   ├── ApiVaultCoordinator.cs  shared unlocked store, tokens, TTL, events
│   ├── SessionJanitor.cs       periodic expiry sweep + relock
│   ├── DiscoveryService.cs     UDP 7241 announcer
│   ├── VaultAuth.cs            bearer filter, no-store filter, rate limiter
│   ├── VaultChangeNotifier.cs  server-wide change events
│   ├── VaultEndpoints.cs       REST surface under /api
│   ├── VaultHub.cs             /hubs/vault hub + relay background service
│   └── WebHostFactory.cs       Kestrel, DI, middleware, embedded assets
├── Services/AuthThrottle.cs    shared master-password failure counter
└── wwwroot/pwa/                manifest, service worker, icons (embedded)
```

## Entry point and modes

```csharp
return Run(args);   // top-level statements

serve = args[0] is "serve" | "--serve" | "--web";
if (!serve) return DoopassTui.Run();
return WebHostFactory.RunWebHost(options);
```

| Invocation | Result |
| ---------- | ------ |
| `doopass` | Terminal UI |
| `doopass serve` | web UI + REST API + hub + discovery on `localhost:7240`, opens the browser |
| `doopass --serve` / `--web` | same as `serve` |
| unknown first argument | treated as "no arguments" → TUI |

### Options (`serve` only)

| Flag | Default | Effect |
| ---- | ------- | ------ |
| `--port N`, `-p N` | `7240` | HTTP/HTTPS port |
| `--no-browser` | browser opens | do not launch the default browser |
| `--lan`, `--network` | loopback | bind `0.0.0.0`; enables discovery announce |
| `--announce` | off | force discovery announce without `--lan` |
| `--tls <pfx>`, `--tls-pfx <pfx>` | none | serve HTTPS from a PKCS#12 file; binds all interfaces |
| `--tls-pass <password>` | none | password for the `.pfx` |

Parsing is permissive: an unknown flag is ignored, a flag missing its value is
ignored, and an unparsable `--port` keeps the default.

TLS precedence: command-line `--tls`/`--tls-pass` override
`settings.json` → `api.tls_pfx` / `api.tls_pass`.

## Startup sequence (`WebHostFactory.BuildWebHost`)

1. `new Settings()` — creates `~/doopass/appdata`, writes `settings.json` on
   first run.
2. `AppLogger.Initialize(settings.LogFile)` — logging is on for everything that
   follows.
3. Resolve TLS: CLI → settings → none. If a `.pfx` is given, it must exist and
   load, otherwise `InvalidOperationException` aborts startup (fail fast beats a
   silently unencrypted host).
4. Bind Kestrel:
   * no TLS → `http://{host}:{port}` where host is `0.0.0.0` with `--lan`,
     `localhost` otherwise;
   * TLS → `o.Listen(IPAddress.Any, port, UseHttps(pfx, pass))`, or a specific
     IP when the (test) `Host` option is an address.
5. Register services (see below).
6. Build, register lifecycle logging, configure middleware and endpoints.
7. Log the URL and, when TLS is active, the certificate's SHA-256 fingerprint
   (the value the phone pins).
8. `RunWebHost` optionally opens the browser and calls `app.Run()`.

`BuildWebHost` is public precisely so the integration tests can run the real
pipeline on `127.0.0.1:0` with a temporary `DOOPASS_HOME`.

## Dependency injection

| Service | Lifetime | Notes |
| ------- | -------- | ----- |
| `Settings` | singleton | shared by the coordinator and the hosted services |
| `IVaultChangeNotifier` → `VaultChangeNotifier` | singleton | server-wide fan-out |
| `AuthThrottle` | singleton | shared master-password failure counter |
| `ApiVaultCoordinator` | singleton | owns the shared store and tokens |
| `DiscoveryAnnounce` | singleton | read by `/api/vault/state` for the fingerprint |
| `DiscoveryService` | hosted | UDP listener |
| `VaultHubRelay` | hosted | forwards notifier events to SignalR |
| `SessionJanitor` | hosted | prunes expired tokens every 30 s |

The browser's UI services (`IVaultSession`, `IVaultEvents`, `ToastService`,
`ISecretClipboard`) are registered by `Doopass.WebClient`, not by the host: the
host serves only static WASM files and the API.

## Middleware and endpoints

```
security headers (CSP, nosniff, DENY, no-referrer)   OnStarting
UseResponseCompression()
UseExceptionHandler("/Error")                        (non-development)
UseRateLimiter()
MapDoopassApi()                                      /api/**
MapHub<VaultHub>("/hubs/vault")
embedded PWA routes                                  /pwa/* /favicon.ico
UseStaticFiles(extracted WASM bundle)                /index.html /_framework/* /_content/*
MapFallback(...)                                     SPA shell for client routes
```

`/api`, `/hubs` and any path containing a dot never fall back to `index.html`;
they stay real 404s.

Embedded assets are read from assembly manifest resources:

| URL | Resource | Cache |
| --- | -------- | ----- |
| `/pwa/sw.js`, `/pwa/manifest.webmanifest` | `Doopass.Cli.wwwroot.pwa.*` | `no-cache` |
| `/pwa/icon-*.png`, `/pwa/apple-touch-icon.png` | `Doopass.Cli.wwwroot.pwa.*` | `max-age=3600` |
| `/pwa/favicon.svg`, `/favicon.ico` | embedded assets | `max-age=3600` |
| `/_framework/*`, `/_content/*`, `/index.html` | extracted WASM bundle | no-cache / compressed |

`sw.js` is served with `Service-Worker-Allowed: /` so a worker under `/pwa/`
can control the whole origin. The csproj removes `wwwroot/**` from published
content: the binary is the asset store.

`PackWasmBundle` publishes `Doopass.WebClient`, zips its `wwwroot/` and embeds
the archive; `ExtractEmbeddedWasmBundle` unpacks it to
`%TEMP%/doopass-web/<version>-<hash>/` on first start.

## Exit codes and lifecycle

* `RunWebHost` returns `0` after a graceful `app.Run()` (Ctrl-C/SIGTERM).
* `DoopassTui.Run()` returns `0` when the TUI exits.
* Startup exceptions (bad TLS file) propagate as an unhandled exception and a
  non-zero exit code.
* Shutdown logs "Doopass web server stopped" via
  `ApplicationStopping`.

## Logging produced by the host

```
INFO Doopass web server starting (v5.1.0, bind localhost, port 7240)
INFO TLS enabled; certificate SHA-256 fingerprint: <hex>
INFO UDP discovery announcing on port 7241          (when enabled)
INFO Doopass web server stopped
ERROR UDP discovery disabled: port 7241 could not be bound
ERROR ...
```

The ASP.NET Core console logger also writes to stdout; the file log is the
durable record.

## Extending the host

* New REST endpoint → add it to `VaultEndpoints.MapDoopassApi`, put the logic in
  `ApiVaultCoordinator`, raise the matching notifier event, and add a test in
  `Doopass.Api.Tests`.
* New server-wide state → register it in `WebHostFactory` and decide the
  lifetime deliberately (singleton = host-wide).
* New static asset → embed it in the owning csproj and map a route with the
  right `Cache-Control`; do not reintroduce a loose `wwwroot` publish.
