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
            Y = 0,
            Width = Dim.Auto(),
            Height = Dim.Auto(),
            SchemeName = Theme.Monochrome.SchemeName,
        };

        const int top = 7;
        const int step = 3;

        Add(
            logo,
            UiKit.Tile("Log in", Pos.Center(), top, () => Shell.Replace(new LoginScreen(Context, Shell))),
            UiKit.Tile("Init store", Pos.Center(), top + step, () => Shell.Replace(new InitStoreScreen(Context, Shell))),
            UiKit.Tile("Backups", Pos.Center(), top + step * 2, () => Shell.Replace(new BackupScreen(Context, Shell))),
            UiKit.Tile("Settings", Pos.Center(), top + step * 3, () => Shell.Replace(new SettingsScreen(Context, Shell))),
            UiKit.Tile("Exit", Pos.Center(), top + step * 4, Shell.Quit));
    }
}
