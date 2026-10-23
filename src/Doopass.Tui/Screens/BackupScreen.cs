using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The backup management screen (create / restore / delete backups).</summary>
internal sealed class BackupScreen : ScreenBase
{
    private const int RowHeight = 3;

    private readonly View _items;
    private readonly List<View> _rows = new();
    private readonly int _baseFocusCount;

    private string Extension => Context.Settings.BackupExtension;

    public BackupScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = UiKit.Title("Backups", 0);
        var subtitle = UiKit.Line("create, restore or delete vault snapshots", 1, dim: true);

        var create = UiKit.Tile("Create backup", 1, 3, CreateBackup, width: 18);
        var back = UiKit.Tile("Back", 20, 3, Shell.GotoMain, width: 9);
        var exit = UiKit.Tile("Exit", 31, 3, Shell.Quit, width: 9);

        _items = new View
        {
            X = 0,
            Y = 7,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = Monochrome.SchemeName,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(title, subtitle, create, back, exit, _items);

        _baseFocusCount = UiKit.RegisteredCount();

        Refresh();
    }

    private void Refresh()
    {
        UiKit.TrimRegistered(_baseFocusCount);

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
                y += RowHeight;
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
            Height = RowHeight;
            CanFocus = true;
            TabStop = TabBehavior.NoStop;
            SchemeName = Monochrome.SchemeName;

            var label = new Label
            {
                Text = name,
                X = 2,
                Y = 1,
                Width = Dim.Fill(20),
                Height = 1,
                SchemeName = Monochrome.SchemeName,
            };

            Add(
                label,
                UiKit.SmallTile("restore", Pos.AnchorEnd(19), 0, onRestore, width: 11),
                UiKit.SmallTile("del", Pos.AnchorEnd(9), 0, onDelete, width: 8));
        }
    }
}
