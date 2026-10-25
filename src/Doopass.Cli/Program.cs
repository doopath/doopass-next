using System.Reflection;
using Doopass.Cli.Services;
using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Diagnostics;
using Doopass.Tui;

// Single entry point for Doopass.
//   doopass                 -> run the terminal UI
//   doopass serve [--port N] [--no-browser] [--lan] -> run the web interface
return Run(args);

static int Run(string[] args)
{
    var serve = args.Length > 0 && args[0] is "serve" or "--serve" or "--web";

    if (!serve)
    {
        return DoopassTui.Run();
    }

    var port = 7240;
    var openBrowser = true;
    var lan = false;

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "--port" or "-p" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
        {
            port = parsed;
            i++;
        }
        else if (args[i] == "--no-browser")
        {
            openBrowser = false;
        }
        else if (args[i] is "--lan" or "--network")
        {
            lan = true;
        }
    }

    return RunWebServer(port, openBrowser, lan);
}

static int RunWebServer(int port, bool openBrowser, bool lan)
{
    var settings = new Settings();
    AppLogger.Initialize(settings.LogFile);

    var host = lan ? "0.0.0.0" : "localhost";
    AppLogger.Info($"Doopass web server starting (v{Constants.Version}, bind {host}, port {port})");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
    });

    builder.WebHost.UseUrls($"http://{host}:{port}");

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddSingleton(settings);
    builder.Services.AddScoped<DoopassSession>();
    builder.Services.AddScoped<Doopass.WebUi.Abstractions.IVaultSession, DoopassSession>();
    builder.Services.AddScoped<Doopass.WebUi.Services.ToastService>();

    var app = builder.Build();

    app.Lifetime.ApplicationStopping.Register(() => AppLogger.Info("Doopass web server stopped"));

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseAntiforgery();

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

    app.MapRazorComponents<Doopass.WebUi.Components.App>()
        .AddInteractiveServerRenderMode();

    var url = $"http://localhost:{port}";
    app.Logger.LogInformation("Doopass web interface: {Url} (bind: {Host})", url, host);

    if (openBrowser)
    {
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

static IResult ServeAsset(HttpContext ctx, Assembly assembly, string resourceName, string contentType, bool revalidate = false)
{
    var stream = assembly.GetManifestResourceStream(resourceName);

    if (stream is null)
    {
        return Results.NotFound();
    }

    ctx.Response.Headers["Cache-Control"] = revalidate ? "no-cache" : "public, max-age=3600";
    return Results.Stream(stream, contentType);
}
