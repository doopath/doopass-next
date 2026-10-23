using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;

namespace Doopass.Tui;

/// <summary>Entry point for the Terminal.Gui (TUI) front-end.</summary>
public static class DoopassTui
{
    /// <summary>Runs the TUI application and blocks until it exits.</summary>
    public static int Run()
    {
#pragma warning disable CS0618 // ConfigurationManager is being replaced; still the standard bootstrapping API.
        ConfigurationManager.Enable(ConfigLocations.All);
#pragma warning restore CS0618

        var settings = new Settings();
        AppLogger.Initialize(settings.LogFile);
        AppLogger.Info($"Doopass TUI started (v{Constants.Version})");

        var context = new DoopassContext(settings);

        using var app = Application.Create();
        app.Init();

        var window = new DoopassWindow(context);
        app.Run(window);

        AppLogger.Info("Doopass TUI exited");
        return 0;
    }
}
