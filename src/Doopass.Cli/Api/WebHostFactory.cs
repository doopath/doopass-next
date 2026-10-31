using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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

        // One pipeline serves both build modes. Dev/folder publish gets the client
        // assets (index.html, _framework/*) from the referenced projects' static web
        // assets; a single-file build gets them extracted to <base>/wwwroot via
        // IncludeAllContentForSelfExtract — UseStaticFiles() serves either with no
        // code split. API (/api/*) and SignalR (/hubs/vault) are mapped above and
        // never reach this fallback.
        app.UseStaticFiles();
        app.UseBlazorFrameworkFiles();
        app.MapFallbackToFile("index.html");

        var scheme = fingerprint is not null ? "https" : "http";
        var url = $"{scheme}://localhost:{options.Port}";
        app.Logger.LogInformation("Doopass web interface: {Url} (bind: {Host})", url, host);

        if (fingerprint is not null)
        {
            app.Logger.LogInformation("TLS certificate SHA-256 fingerprint: {Fingerprint}", fingerprint);
        }

        return app;
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
            var scheme = options.TlsPfx is not null ? "https" : "http";
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
}
