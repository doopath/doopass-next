using Doopass.Core;
using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The main menu: log in, init store, backups, settings, exit.</summary>
internal sealed class MainMenuScreen : ScreenBase
{
    private static readonly (string Label, Action<MainMenuScreen> Run)[] Items =
    [
        ("Log in         unlock an existing vault", s => s.Shell.Replace(new LoginScreen(s.Context, s.Shell))),
        ("Init store     create a new encrypted vault", s => s.Shell.Replace(new InitStoreScreen(s.Context, s.Shell))),
        ("Backups        create, restore or delete snapshots", s => s.Shell.Replace(new BackupScreen(s.Context, s.Shell))),
        ("Settings       application configuration", s => s.Shell.Replace(new SettingsScreen(s.Context, s.Shell))),
        ("Exit           close doopass", s => s.Shell.Quit()),
    ];

    private readonly List<Label> _rows = new();

    public MainMenuScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var logo = new Label
        {
            Text = Constants.Logo,
            X = Pos.Center(),
            Y = 0,
            Width = Dim.Auto(),
            Height = Dim.Auto(),
            SchemeName = Monochrome.SchemeName,
        };

        var hint = UiKit.Hint("choose an action", 7);
        hint.X = Pos.Center();
        hint.Width = Dim.Auto();

        for (var i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var row = UiKit.MenuItem(item.Label, Pos.Center(), 9 + i, () => item.Run(this), width: 54);

            _rows.Add(AddStop(row));
            Add(row);
        }

        var tagline = UiKit.Hint("local encrypted password store · doopass", 15);
        tagline.X = Pos.Center();
        tagline.Width = Dim.Auto();

        Add(logo, hint, tagline);
    }

    public override string ScreenTitle => "main menu";

    public override string FooterHint => "j/k move   enter select   f1 help   q quit";
}
