using Doopass.Cli.Services;
using Doopass.Core.Configuration;
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
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
    });

    var host = lan ? "0.0.0.0" : "localhost";
    builder.WebHost.UseUrls($"http://{host}:{port}");

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddSingleton(_ => new Settings());
    builder.Services.AddScoped<DoopassSession>();
    builder.Services.AddScoped<ToastService>();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseAntiforgery();

    // Serve the web assets from embedded resources (works in single-file deployments too).
    app.MapGet("/app.css", (HttpContext ctx) => ServeAsset(ctx, "Doopass.Cli.wwwroot.app.css", "text/css; charset=utf-8"));
    app.MapGet("/theme.js", (HttpContext ctx) => ServeAsset(ctx, "Doopass.Cli.wwwroot.theme.js", "text/javascript; charset=utf-8"));
    app.MapGet("/_framework/blazor.web.js", (HttpContext ctx) => ServeAsset(ctx, "Doopass.Cli.wwwroot.blazor.web.js", "text/javascript; charset=utf-8"));

    app.MapRazorComponents<Doopass.Cli.Components.App>()
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

static IResult ServeAsset(HttpContext ctx, string resourceName, string contentType)
{
    var stream = typeof(Doopass.Cli.Components.App).Assembly.GetManifestResourceStream(resourceName);

    if (stream is null)
    {
        return Results.NotFound();
    }

    ctx.Response.Headers["Cache-Control"] = "public, max-age=3600";
    return Results.Stream(stream, contentType);
}
