using Doopass.Core;
using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The main menu: log in, init store, backups, settings, exit.</summary>
internal sealed class MainMenuScreen : ScreenBase
{
    private readonly List<Label> _rows = new();
    private Label? _primaryRow;

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

        // Row 0 opens the store directly when the vault is already unlocked, so
        // going back from the store never asks for the password again.
        var items = new List<(string Label, Action Run)>
        {
            (PrimaryLabel(), OpenPrimary),
            ("Init store     create a new encrypted vault", () => Shell.Replace(new InitStoreScreen(Context, Shell))),
            ("Backups        create, restore or delete snapshots", () => Shell.Replace(new BackupScreen(Context, Shell))),
            ("Settings       application configuration", () => Shell.Replace(new SettingsScreen(Context, Shell))),
            ("Exit           close doopass", Shell.Quit),
        };

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = UiKit.MenuItem(item.Label, Pos.Center(), 9 + i, item.Run, width: 54);

            _rows.Add(AddStop(row));
            Add(row);

            if (i == 0)
            {
                _primaryRow = row;
            }
        }

        var tagline = UiKit.Hint("local encrypted password store · doopass", 15);
        tagline.X = Pos.Center();
        tagline.Width = Dim.Auto();

        Add(logo, hint, tagline);
    }

    public override string ScreenTitle => "main menu";

    public override string FooterHint => "h/j/k/l move   enter select   f1 help   esc quit";

    public override void OnShown()
    {
        if (_primaryRow is not null)
        {
            _primaryRow.Text = "  " + PrimaryLabel();
        }
    }

    private string PrimaryLabel() => Context.CurrentStore is not null
        ? "Open store     unlocked - no password needed"
        : "Log in         unlock an existing vault";

    private void OpenPrimary()
    {
        if (Context.CurrentStore is not null)
        {
            Shell.Replace(new StoreScreen(Context, Shell));
            return;
        }

        Shell.Replace(new LoginScreen(Context, Shell));
    }
}
