using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Doopass.Cli.Services;
using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;

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
/// Builds (and runs) the Doopass web host: Blazor Server UI, REST API under
/// /api, the vault SignalR hub and LAN discovery. Split from <c>Program.cs</c>
/// so integration tests can build the same pipeline on a random port.
/// </summary>
public static class WebHostFactory
{
    /// <summary>
    /// Builds the web host (Blazor UI, REST API, SignalR hub and discovery)
    /// without starting it, so tests can run the real pipeline on a free port.
    /// </summary>
    /// <param name="options">Port, bind address, TLS and discovery options.</param>
    /// <returns>A configured, not yet started application.</returns>
    /// <exception cref="InvalidOperationException">The TLS certificate is missing or cannot be loaded.</exception>
    public static WebApplication BuildWebHost(WebHostOptions options)
    {
        var settings = new Settings();
        AppLogger.Initialize(settings.LogFile);

        var host = options.Host ?? (options.Lan ? "0.0.0.0" : "localhost");
        AppLogger.Info($"Doopass web server starting (v{Constants.Version}, bind {host}, port {options.Port})");

        // --tls on the command line wins over the settings.json designation.
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
            // TLS mode: bind Kestrel explicitly with the caller-supplied certificate.
            // Remote clients are the whole point of --tls, so it binds all interfaces
            // unless the (test) host override carries a bindable IP address.
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

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IVaultChangeNotifier, VaultChangeNotifier>();
        builder.Services.AddSingleton<ApiVaultCoordinator>();

        // One session type per circuit; IVaultSession / IVaultEvents resolve to
        // the same scoped instances the circuit actually lives on.
        builder.Services.AddScoped<VaultEventRelay>();
        builder.Services.AddScoped<DoopassSession>();
        builder.Services.AddScoped<IVaultSession>(sp => sp.GetRequiredService<DoopassSession>());
        builder.Services.AddScoped<IVaultEvents>(sp => sp.GetRequiredService<VaultEventRelay>());
        builder.Services.AddScoped<Doopass.WebUi.Services.ToastService>();
        builder.Services.AddScoped<ISecretClipboard, Doopass.WebUi.Services.JsClipboard>();

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
        // Registered for the state endpoint as well: the trust-on-first-use flow
        // on thin clients reads the fingerprint from /api/vault/state.
        builder.Services.AddSingleton(announce);

        builder.Services.AddHostedService<VaultHubRelay>();

        var app = builder.Build();

        app.Lifetime.ApplicationStopping.Register(() => AppLogger.Info("Doopass web server stopped"));

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();
        app.UseRateLimiter();

        app.MapDoopassApi();
        app.MapHub<VaultHub>("/hubs/vault");

        // Serve the web assets from embedded resources (works in single-file deployments too).
        // The root UI assets live in the Doopass.WebUi RCL; the web host owns the framework
        // script and PWA assets. The asset URLs carry no content hash, so they must be
        // revalidated on every load -- otherwise a browser keeps running a stale theme.js /
        // app.css for an hour after an upgrade.
        var webUi = typeof(Doopass.WebUi.Components.App).Assembly;
        var cli = typeof(DoopassSession).Assembly;
        app.MapGet("/app.css", (HttpContext ctx) => ServeAsset(ctx, webUi, "Doopass.WebUi.wwwroot.app.css", "text/css; charset=utf-8", revalidate: true));
        app.MapGet("/theme.js", (HttpContext ctx) => ServeAsset(ctx, webUi, "Doopass.WebUi.wwwroot.theme.js", "text/javascript; charset=utf-8", revalidate: true));
        app.MapGet("/app.js", (HttpContext ctx) => ServeAsset(ctx, webUi, "Doopass.WebUi.wwwroot.app.js", "text/javascript; charset=utf-8", revalidate: true));
        // Brand mark for the shared layouts. Served at the same _content/... URL the
        // MAUI WebView resolves, so MainLayout / AuthLayout keep one image source in
        // both hosts instead of branching on which one is rendering.
        app.MapGet("/_content/Doopass.WebUi/brand.svg", (HttpContext ctx) =>
            ServeAsset(ctx, webUi, "Doopass.WebUi.wwwroot.brand.svg", "image/svg+xml; charset=utf-8", revalidate: true));
        app.MapGet("/_framework/blazor.web.js", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.blazor.web.js", "text/javascript; charset=utf-8"));

        // PWA: installable web app (manifest, service worker, icons), all embedded.
        app.MapGet("/pwa/manifest.webmanifest", (HttpContext ctx) =>
            ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.manifest.webmanifest", "application/manifest+json; charset=utf-8", revalidate: true));
        app.MapGet("/pwa/sw.js", (HttpContext ctx) =>
        {
            // Lets a worker under /pwa/ claim the whole site ("https://host/") as scope.
            ctx.Response.Headers["Service-Worker-Allowed"] = "/";
            return ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.sw.js", "text/javascript; charset=utf-8", revalidate: true);
        });
        app.MapGet("/pwa/icon-192.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-192.png", "image/png"));
        app.MapGet("/pwa/icon-512.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-512.png", "image/png"));
        app.MapGet("/pwa/icon-maskable-192.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-maskable-192.png", "image/png"));
        app.MapGet("/pwa/icon-maskable-512.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.icon-maskable-512.png", "image/png"));
        app.MapGet("/pwa/apple-touch-icon.png", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.apple-touch-icon.png", "image/png"));

        // Browser icon: the SVG master is what modern browsers use, the .ico covers
        // legacy agents and the /favicon.ico probe that some of them make anyway.
        app.MapGet("/pwa/favicon.svg", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.favicon.svg", "image/svg+xml; charset=utf-8"));
        app.MapGet("/pwa/favicon.ico", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.favicon.ico", "image/x-icon"));
        app.MapGet("/favicon.ico", (HttpContext ctx) => ServeAsset(ctx, cli, "Doopass.Cli.wwwroot.pwa.favicon.ico", "image/x-icon"));

        app.MapRazorComponents<Doopass.WebUi.Components.App>()
            .AddInteractiveServerRenderMode();

        var scheme = fingerprint is not null ? "https" : "http";
        var url = $"{scheme}://localhost:{options.Port}";
        app.Logger.LogInformation("Doopass web interface: {Url} (bind: {Host})", url, host);

        if (fingerprint is not null)
        {
            // Trust-on-first-use: clients pin this SHA-256 certificate fingerprint.
            app.Logger.LogInformation("TLS certificate SHA-256 fingerprint: {Fingerprint}", fingerprint);
        }

        return app;
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

    private static string? ReadApiSetting(Settings settings, string key)
    {
        if (!settings.Sections.Contains("api", StringComparer.Ordinal))
        {
            return null;
        }

        return settings.GetSection("api").TryGetValue(key, out var value) && value.Length > 0 ? value : null;
    }

    /// <summary>Checks the TLS certificate exists and is loadable; returns its SHA-256 fingerprint.</summary>
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
}
