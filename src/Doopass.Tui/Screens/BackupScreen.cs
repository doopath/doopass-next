using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The backup management screen (create / restore / delete backups).</summary>
internal sealed class BackupScreen : ScreenBase
{
    private readonly View _items;
    private readonly string _extension;
    private readonly List<View> _rows = new();

    public BackupScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        _extension = Context.Settings.BackupExtension;

        var create = MakeButton("Create backup", 0, 0, 16, () =>
        {
            Context.Backups.MakeBackup();
            Refresh();
        });

        var toMain = MakeButton("To Main Menu", 18, 0, 15, Shell.GotoMain);
        var exit = MakeButton("Exit", 35, 0, 8, Shell.Quit);

        _items = new View
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(create, toMain, exit, _items);

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
        var y = 0;

        _items.Add(new Label
        {
            Text = names.Count > 0 ? "Your backups" : "No backups",
            X = 0,
            Y = y,
            Width = Dim.Fill(),
        });
        y += 1;

        foreach (var name in names)
        {
            var row = new BackupRow(
                name,
                () => Restore(name),
                () => Delete(name))
            {
                X = 0,
                Y = y,
            };

            _items.Add(row);
            _rows.Add(row);
            y += 1;
        }

        App?.LayoutAndDraw();
    }

    private void Restore(string name)
    {
        Context.Backups.SwapStoreWithBackup(name + _extension);
        Context.ReloadStore();
        Refresh();
    }

    private void Delete(string name)
    {
        Context.Backups.DeleteBackup(name + _extension);
        Refresh();
    }

    private static Button MakeButton(string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, X = x, Y = y, Width = width };
        button.Accepted += (_, _) => action();
        return button;
    }

    private sealed class BackupRow : View
    {
        public BackupRow(string name, Action onRestore, Action onDelete)
        {
            Width = Dim.Fill();
            Height = 1;

            var label = new Label { Text = $" {name}", X = 0, Y = 0, Width = Dim.Fill(24) };

            var restore = new Button { Text = "Restore", X = Pos.AnchorEnd(22), Y = 0, Width = 10 };
            var delete = new Button { Text = "Delete", X = Pos.AnchorEnd(11), Y = 0, Width = 10 };

            restore.Accepted += (_, _) => onRestore();
            delete.Accepted += (_, _) => onDelete();

            Add(label, restore, delete);
        }
    }
}
