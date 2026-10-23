using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Diagnostics;
using Doopass.Tui.Theme;
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
        Monochrome.Register();

        var window = new DoopassWindow(context);

        // Global hotkeys must win over application-scope Tab/arrow bindings and over
        // whatever control currently has focus.
        app.Keyboard.KeyDown += (_, key) => window.RouteKey(key, app);

        // The first iteration runs after the application has laid the window out and
        // focused its container; hand focus to the screen's own first control then.
        var focusAsserted = false;
        app.Iteration += (_, _) =>
        {
            if (focusAsserted)
            {
                return;
            }

            focusAsserted = true;
            window.FocusCurrent();
        };

        app.Run(window);

        AppLogger.Info("Doopass TUI exited");
        return 0;
    }
}
