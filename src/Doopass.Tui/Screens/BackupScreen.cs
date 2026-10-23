using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The backup management screen (create / restore / delete backups).</summary>
internal sealed class BackupScreen : ScreenBase
{
    private readonly View _items;
    private readonly List<View> _rows = new();

    private string Extension => Context.Settings.BackupExtension;

    public BackupScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = UiKit.Title("Backups", 0);
        var subtitle = UiKit.Line("create, restore or delete vault snapshots", 1, dim: true);

        var create = UiKit.InlineButton("Create backup", 1, 3, 18, CreateBackup);
        var back = UiKit.InlineButton("Back", 20, 3, 9, Shell.GotoMain);
        var exit = UiKit.InlineButton("Exit", 31, 3, 9, Shell.Quit);

        _items = new View
        {
            X = 0,
            Y = 5,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = Monochrome.SchemeName,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(title, subtitle, create, back, exit, _items);

        Refresh();
    }

    private void Refresh()
    {
        foreach (var row in _rows)
        {
            _items.Remove(row);
            row.Dispose();
        }

        _rows.Clear();

        var names = Context.Backups.GetBackupNames();

        if (names.Count == 0)
        {
            var empty = new Label
            {
                Text = "no backups yet",
                X = Pos.Center(),
                Y = 1,
                Width = Dim.Auto(),
                Height = 1,
                SchemeName = Monochrome.DimSchemeName,
            };

            _items.Add(empty);
            _rows.Add(empty);
        }
        else
        {
            var y = 0;

            foreach (var name in names)
            {
                var row = new BackupRow(name, () => ConfirmRestore(name), () => ConfirmDelete(name))
                {
                    X = 0,
                    Y = y,
                };

                _items.Add(row);
                _rows.Add(row);
                y++;
            }
        }

        App?.LayoutAndDraw();
    }

    private void CreateBackup()
    {
        Context.Backups.MakeBackup();
        Refresh();
    }

    private void ConfirmRestore(string name)
    {
        Shell.Confirm($"Restore vault from '{name}'?", () =>
        {
            Context.Backups.SwapStoreWithBackup(name + Extension);
            Context.ReloadStore();
            Shell.CloseAndReplace(new BackupScreen(Context, Shell));
        });
    }

    private void ConfirmDelete(string name)
    {
        Shell.Confirm($"Delete backup '{name}'?", () =>
        {
            Context.Backups.DeleteBackup(name + Extension);
            Shell.CloseAndReplace(new BackupScreen(Context, Shell));
        });
    }

    private sealed class BackupRow : View
    {
        public BackupRow(string name, Action onRestore, Action onDelete)
        {
            Width = Dim.Fill();
            Height = 1;
            CanFocus = true;
            TabStop = TabBehavior.NoStop;
            SchemeName = Monochrome.SchemeName;

            var label = new Label
            {
                Text = name,
                X = 1,
                Y = 0,
                Width = Dim.Fill(18),
                Height = 1,
                SchemeName = Monochrome.SchemeName,
            };

            Add(
                label,
                UiKit.InlineButton("restore", Pos.AnchorEnd(18), 0, 10, onRestore),
                UiKit.InlineButton("del", Pos.AnchorEnd(8), 0, 8, onDelete));
        }
    }
}
