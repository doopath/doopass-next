using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Doopass.Cli.Services;
using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Doopass.Cli.Api;

/// <summary>Options for building the web host: ports, TLS, announce and browser behavior.</summary>
public sealed record WebHostOptions
{
    /// <summary>TCP port for the web UI and the API (default <c>7240</c>).</summary>
    public int Port { get; init; } = 7240;

    /// <summary>Bind all interfaces instead of loopback and enable LAN discovery.</summary>
    public bool Lan { get; init; }

    /// <summary>Bind host override (tests bind 127.0.0.1); when null LAN binds 0.0.0.0, otherwise localhost.</summary>
    public string? Host { get; init; }

    /// <summary>Open the default browser on startup.</summary>
    public bool OpenBrowser { get; init; } = true;

    /// <summary>Path to the server TLS certificate (.pfx); null keeps plain HTTP.</summary>
    public string? TlsPfx { get; init; }

    /// <summary>Password for <see cref="TlsPfx"/>; null when the file is unprotected.</summary>
    public string? TlsPassword { get; init; }

    /// <summary>Force LAN discovery announce even without --lan.</summary>
    public bool Announce { get; init; }
}

/// <summary>
/// Builds (and runs) the Doopass web host: static Blazor WebAssembly UI, REST API under
/// /api, the vault SignalR hub and LAN discovery. Split from <c>Program.cs</c>
/// so integration tests can build the same pipeline on a random port.
/// </summary>
public static class WebHostFactory
{
    /// <summary>
    /// Builds the web host (static WASM UI, REST API, SignalR hub and discovery)
    /// without starting it, so tests can run the real pipeline on a free port.
    /// </summary>
    public static WebApplication BuildWebHost(WebHostOptions options)
    {
        var settings = new Settings();
        AppLogger.Initialize(settings.LogFile);

        var host = options.Host ?? (options.Lan ? "0.0.0.0" : "localhost");
        AppLogger.Info($"Doopass web server starting (v{Constants.Version}, bind {host}, port {options.Port})");

        var tlsPfx = options.TlsPfx ?? ReadApiSetting(settings, "tls_pfx");
        var tlsPassword = options.TlsPassword ?? ReadApiSetting(settings, "tls_pass");
        var fingerprint = ValidateTlsCertificate(tlsPfx, tlsPassword);

        if (options.Lan && tlsPfx is null)
        {
            // --lan is explicit, so it stays allowed, but the user must know what it
            // exposes. --tls binds 0.0.0.0 on its own, so it needs no --lan.
            const string warning =
                "WARNING: plaintext HTTP on the LAN - the master password and all secrets travel unencrypted. " +
                "Use --tls <pfx> for encrypted access (--tls already binds 0.0.0.0, no --lan needed).";
            Console.Error.WriteLine(warning);
            AppLogger.Error(warning);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
        });

        if (tlsPfx is null)
        {
            builder.WebHost.UseUrls($"http://{host}:{options.Port}");
        }
        else
        {
            IPAddress bindAddress = IPAddress.Any;

            if (options.Host is not null && IPAddress.TryParse(options.Host, out var parsed))
            {
                bindAddress = parsed;
            }

            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Listen(bindAddress, options.Port,
                    listen => listen.UseHttps(tlsPfx, tlsPassword));
            });
        }

        // Server-side services: coordinator + notifier.
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IVaultChangeNotifier, VaultChangeNotifier>();
        builder.Services.AddSingleton<AuthThrottle>();
        builder.Services.AddSingleton<ApiVaultCoordinator>();

        builder.Services.AddSignalR();
        VaultAuth.AddVaultRateLimiter(builder.Services);

        var announceEnabled = options.Lan || options.Announce
            || string.Equals(ReadApiSetting(settings, "announce"), "true", StringComparison.OrdinalIgnoreCase);
        var serverName = Environment.MachineName is { Length: > 0 } machine ? machine : "doopass";
        var announce = new DiscoveryAnnounce(serverName, options.Port, fingerprint is not null, fingerprint, Constants.Version)
        {
            Enabled = announceEnabled,
        };
        builder.Services.AddHostedService(_ => new DiscoveryService(announce));
        builder.Services.AddSingleton(announce);

        builder.Services.AddHostedService<VaultHubRelay>();

        var app = builder.Build();

        app.Lifetime.ApplicationStopping.Register(() => AppLogger.Info("Doopass web server stopped"));

        // Security headers for every response (assets, API, hub handshake, pages).
        // Applied in OnStarting so they are written even after the static-file
        // pipeline, and with the indexer so a header the framework already set
        // is replaced rather than duplicated. style-src needs 'unsafe-inline'
        // because Razor pages carry inline style="..." attributes; script-src
        // needs 'wasm-unsafe-eval' for the Blazor WebAssembly runtime.
        var usesTls = tlsPfx is not null;
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
                headers["Content-Security-Policy"] =
                    "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
                    "script-src 'self' 'wasm-unsafe-eval'; connect-src 'self' ws: wss:; " +
                    "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

                if (usesTls && ShouldSendHsts(context.Request.Host.Host))
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000";
                }

                return Task.CompletedTask;
            });

            return next(context);
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        // API paths are JSON-first and must keep their own error shape; the SPA
        // fallback below is only for client routes, so status-code pages are not
        // used (a stray client path falling back to index.html is fine).
        app.UseRateLimiter();

        // --- REST API + SignalR hub ------------------------------------------

        app.MapDoopassApi();
        app.MapHub<VaultHub>("/hubs/vault");

        // --- PWA assets (embedded resources) ----------------------------------

        var cli = typeof(WebHostFactory).Assembly;

        // PWA: installable web app (manifest, service worker, icons).
        app.MapGet("/pwa/manifest.webmanifest", (HttpContext ctx) =>
            ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.manifest.webmanifest", "application/manifest+json; charset=utf-8", revalidate: true));
        app.MapGet("/pwa/sw.js", (HttpContext ctx) =>
        {
            ctx.Response.Headers["Service-Worker-Allowed"] = "/";
            return ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.sw.js", "text/javascript; charset=utf-8", revalidate: true);
        });
        app.MapGet("/pwa/icon-192.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-192.png", "image/png"));
        app.MapGet("/pwa/icon-512.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-512.png", "image/png"));
        app.MapGet("/pwa/icon-maskable-192.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-maskable-192.png", "image/png"));
        app.MapGet("/pwa/icon-maskable-512.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-maskable-512.png", "image/png"));
        app.MapGet("/pwa/apple-touch-icon.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.apple-touch-icon.png", "image/png"));

        // Browser icon.
        app.MapGet("/pwa/favicon.svg", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.favicon.svg", "image/svg+xml; charset=utf-8"));
        app.MapGet("/pwa/favicon.ico", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.favicon.ico", "image/x-icon"));
        app.MapGet("/favicon.ico", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.favicon.ico", "image/x-icon"));

        // --- Static WASM UI + SPA fallback ------------------------------------

        // The client bundle is embedded in this assembly by the PackWasmBundle
        // MSBuild target, so a single-file executable never needs a loose
        // wwwroot/ directory. Folder builds and dev runs use the same extracted
        // bundle; if extraction is unavailable (design-time build), fall back to
        // the SDK's static web assets.
        var wasmRoot = ExtractEmbeddedWasmBundle();
        IFileProvider? wasmProvider = null;

        if (wasmRoot is not null)
        {
            wasmProvider = new PhysicalFileProvider(wasmRoot);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = wasmProvider,
                ContentTypeProvider = WasmContentTypes,
            });
        }
        else
        {
            app.UseStaticFiles();
            app.UseBlazorFrameworkFiles();
            wasmProvider = app.Environment.WebRootFileProvider;
        }

        MapSpaFallback(app, wasmProvider);

        var scheme = fingerprint is not null ? "https" : "http";
        ((IApplicationBuilder)app).Properties[SchemeProperty] = scheme;
        var url = $"{scheme}://localhost:{options.Port}";
        app.Logger.LogInformation("Doopass web interface: {Url} (bind: {Host})", url, host);

        if (fingerprint is not null)
        {
            app.Logger.LogInformation("TLS certificate SHA-256 fingerprint: {Fingerprint}", fingerprint);
        }

        return app;
    }

    private const string WasmBundleResource = "Doopass.Cli.wwwroot.wasm.zip";

    /// <summary>App property carrying the scheme the host actually serves.</summary>
    private const string SchemeProperty = "Doopass.Scheme";

    private static readonly FileExtensionContentTypeProvider WasmContentTypes = CreateWasmContentTypes();

    private static FileExtensionContentTypeProvider CreateWasmContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".wasm"] = "application/wasm";
        provider.Mappings[".dat"] = "application/octet-stream";
        provider.Mappings[".blazor"] = "text/plain; charset=utf-8";
        provider.Mappings[".webcil"] = "application/octet-stream";
        provider.Mappings[".dll"] = "application/octet-stream";
        return provider;
    }

    /// <summary>
    /// Extracts the embedded WASM bundle to a version-addressed temp directory and
    /// returns its path. A marker file makes repeat starts cheap; failures fall back
    /// to the SDK static web assets instead of taking the host down.
    /// </summary>
    private static string? ExtractEmbeddedWasmBundle()
    {
        var assembly = typeof(WebHostFactory).Assembly;
        using var resource = assembly.GetManifestResourceStream(WasmBundleResource);

        if (resource is null)
        {
            return null;
        }

        try
        {
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant()[..16];
            var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var root = Path.Combine(Path.GetTempPath(), "doopass-web", $"{version}-{hash}");
            var marker = Path.Combine(root, ".complete");

            if (!File.Exists(marker))
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                Directory.CreateDirectory(root);
                buffer.Position = 0;
                using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
                archive.ExtractToDirectory(root);
                File.WriteAllText(marker, hash);
            }

            return root;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Extracting the embedded web UI bundle failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Serves index.html for client-side routes, but leaves API, hub, PWA and
    /// file-shaped paths as real 404s instead of masking them with the SPA shell.
    /// </summary>
    private static void MapSpaFallback(WebApplication app, IFileProvider provider)
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;

            if (path.StartsWithSegments("/api")
                || path.StartsWithSegments("/hubs")
                || path.StartsWithSegments("/pwa")
                || (path.Value?.Contains('.') ?? false))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var index = provider.GetFileInfo("index.html");

            if (!index.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
        });
    }

    private static string? ReadApiSetting(Settings settings, string key)
    {
        if (!settings.Sections.Contains("api", StringComparer.Ordinal))
        {
            return null;
        }

        return settings.GetSection("api").TryGetValue(key, out var value) && value.Length > 0 ? value : null;
    }

    private static string? ValidateTlsCertificate(string? tlsPfx, string? tlsPassword)
    {
        if (string.IsNullOrEmpty(tlsPfx))
        {
            return null;
        }

        if (!File.Exists(tlsPfx))
        {
            throw new InvalidOperationException($"TLS certificate file not found: {tlsPfx}");
        }

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(tlsPfx, tlsPassword);
        var fingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        AppLogger.Info($"TLS enabled; certificate SHA-256 fingerprint: {fingerprint}");
        return fingerprint;
    }

    private static IResult ServeAsset(HttpContext ctx, System.Reflection.Assembly assembly, string resourceName, string contentType, bool revalidate = false)
    {
        var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            return Results.NotFound();
        }

        ctx.Response.Headers["Cache-Control"] = revalidate ? "no-cache" : "public, max-age=3600";
        return Results.Stream(stream, contentType);
    }

    /// <summary>Runs a built host to completion (SIGTERM/CTRL-C handled by the runtime).</summary>
    public static int RunWebHost(WebHostOptions options)
    {
        var app = BuildWebHost(options);

        if (options.OpenBrowser)
        {
            // The certificate may come from settings.json, not the CLI flag, so
            // use the scheme BuildWebHost actually resolved.
            var scheme = ((IApplicationBuilder)app).Properties.TryGetValue(SchemeProperty, out var resolved)
                ? (string)resolved!
                : "http";
            var url = $"{scheme}://localhost:{options.Port}";

            _ = Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true,
                    });
                }
                catch
                {
                    // Ignore: the user can open the URL manually.
                }
            });
        }

        app.Run();
        return 0;
    }
    /// <summary>
    /// HSTS may only be pinned for a real network host. If a browser remembers it
    /// for localhost, the next plain <c>http://localhost</c> run is upgraded to
    /// https against a certificate that run does not even serve.
    /// </summary>
    internal static bool ShouldSendHsts(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        // HostString keeps IPv6 literals without brackets, but accept both forms.
        var normalized = host.Trim('[', ']');

        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(normalized, out var address) || !IPAddress.IsLoopback(address);
    }
}
