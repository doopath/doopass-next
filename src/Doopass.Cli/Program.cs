using Doopass.Cli.Services;
using Doopass.Core.Configuration;
using Doopass.Tui;
using Microsoft.FluentUI.AspNetCore.Components;

// Single entry point for Doopass.
//   doopass                 -> run the terminal UI
//   doopass serve [--port N] [--no-browser] -> run the Fluent web interface
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
    }

    return RunWebServer(port, openBrowser);
}

static int RunWebServer(int port, bool openBrowser)
{
    // .NET 10 serves framework static assets (e.g. _framework/blazor.web.js) through the
    // "development" static-web-assets handler when running from a non-published build.
    // `dotnet run` enables Development via launchSettings.json; when the app is launched
    // directly from the build output we default to Development so the browser UI works.
    // Published builds have no runtime manifest and keep the configured environment.
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "Doopass.staticwebassets.runtime.json")))
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
    }

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
    });

    builder.WebHost.UseUrls($"http://localhost:{port}");

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddFluentUIComponents();
    builder.Services.AddSingleton(_ => new Settings());
    builder.Services.AddScoped<DoopassSession>();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseAntiforgery();
    app.MapStaticAssets();
    app.MapRazorComponents<Doopass.Cli.Components.App>()
        .AddInteractiveServerRenderMode();

    var url = $"http://localhost:{port}";
    app.Logger.LogInformation("Doopass web interface: {Url}", url);

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
