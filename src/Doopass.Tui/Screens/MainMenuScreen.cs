using Doopass.Core;
using Doopass.Tui.Ui;
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
            SchemeName = Theme.Monochrome.SchemeName,
        };

        const int top = 9;
        const int step = 2;

        Add(
            logo,
            UiKit.MenuButton("Log in", top, () => Shell.Replace(new LoginScreen(Context, Shell))),
            UiKit.MenuButton("Init store", top + step, () => Shell.Replace(new InitStoreScreen(Context, Shell))),
            UiKit.MenuButton("Backups", top + step * 2, () => Shell.Replace(new BackupScreen(Context, Shell))),
            UiKit.MenuButton("Settings", top + step * 3, () => Shell.Replace(new SettingsScreen(Context, Shell))),
            UiKit.MenuButton("Exit", top + step * 4, Shell.Quit));
    }
}
