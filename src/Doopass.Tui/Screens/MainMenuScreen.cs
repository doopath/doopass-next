using Doopass.Core;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The main menu: log in, init store, backups, settings, exit.</summary>
internal sealed class MainMenuScreen : ScreenBase
{
    public MainMenuScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var logo = new Label
        {
            Text = Constants.Logo,
            X = Pos.Center(),
            Y = 1,
            Width = Dim.Auto(),
            Height = Dim.Auto(),
        };

        var logIn = MakeButton("Log in", 10, () => Shell.Replace(new LoginScreen(Context, Shell)));
        var initStore = MakeButton("Init store", 12, () => Shell.Replace(new InitStoreScreen(Context, Shell)));
        var backups = MakeButton("Backups", 14, () => Shell.Replace(new BackupScreen(Context, Shell)));
        var settings = MakeButton("Settings", 16, () => Shell.Replace(new SettingsScreen(Context, Shell)));
        var exit = MakeButton("Exit", 18, Shell.Quit);

        Add(logo, logIn, initStore, backups, settings, exit);
    }

    private static Button MakeButton(string text, int y, Action action)
    {
        var button = new Button
        {
            Text = text,
            X = Pos.Center(),
            Y = y,
            Width = 24,
        };

        button.Accepted += (_, _) => action();
        return button;
    }
}
