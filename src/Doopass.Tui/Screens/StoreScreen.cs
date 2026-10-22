using Doopass.Core;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The store screen: search + list of key/value pairs with actions.</summary>
internal sealed class StoreScreen : ScreenBase
{
    private readonly TextField _searchField;
    private readonly View _items;
    private readonly List<StoreRow> _rows = new();
    private string _filter = string.Empty;

    public StoreScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var addPair = MakeButton("Add pair", 0, 0, 13, AddPair);
        var toMain = MakeButton("To Main Menu", 16, 0, 15, Shell.GotoMain);
        var exit = MakeButton("Exit", 33, 0, 8, Shell.Quit);

        _searchField = new TextField { X = 1, Y = 2, Width = 30 };
        var search = MakeButton("Search", 33, 2, 10, () => ApplyFilter(_searchField.Text ?? string.Empty));
        var clean = MakeButton("Clean search", 45, 2, 14, CleanSearch);

        var title = new Label { Text = "Your keys", X = 1, Y = 4, Width = Dim.Fill() };

        _items = new View
        {
            X = 0,
            Y = 5,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(addPair, toMain, exit, _searchField, search, clean, title, _items);

        _searchField.Accepted += (_, _) => ApplyFilter(_searchField.Text ?? string.Empty);

        ApplyFilter(string.Empty);
    }

    private void AddPair()
    {
        Shell.Push(new PairFormScreen(
            Context,
            Shell,
            "Key",
            "Value",
            string.Empty,
            string.Empty,
            "Add pair",
            (key, value) =>
            {
                try
                {
                    Context.CurrentStore!.AddProperty(key, value);
                    Shell.Replace(new StoreScreen(Context, Shell));
                }
                catch (PropertyAlreadyExistError ex)
                {
                    MessageBox.Query(App!, "Doopass", ex.Message, "OK");
                }
            }));
    }

    private void CopyKey(string key)
    {
        var value = Context.CurrentStore!.GetValue(key);
        SetClipboard(value);
    }

    private void EditKey(string key)
    {
        var value = Context.CurrentStore!.GetValue(key);

        Shell.Push(new PairFormScreen(
            Context,
            Shell,
            "Key",
            "Value",
            key,
            value,
            "Update",
            (newKey, newValue) =>
            {
                try
                {
                    var store = Context.CurrentStore!;
                    store.RemoveProperty(key);
                    store.AddProperty(newKey, newValue);
                    Shell.Replace(new StoreScreen(Context, Shell));
                }
                catch (Exception ex) when (ex is PropertyDoesNotExistError or PropertyAlreadyExistError)
                {
                    MessageBox.Query(App!, "Doopass", ex.Message, "OK");
                }
            }));
    }

    private void DeleteKey(string key)
    {
        var value = Context.CurrentStore!.GetValue(key);
        SetClipboard($"{key}={value}");
        Context.CurrentStore!.RemoveProperty(key);
        ApplyFilter(_filter);
    }

    private void SetClipboard(string text)
    {
        if (App is { } app)
        {
            _ = app.Clipboard?.TrySetClipboardData(text);
        }
    }

    private void CleanSearch()
    {
        _searchField.Text = string.Empty;
        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string filter)
    {
        _filter = filter;

        foreach (var row in _rows)
        {
            _items.Remove(row);
            row.Dispose();
        }

        _rows.Clear();

        var keys = Context.CurrentStore?.GetKeys() ?? [];
        var ordered = keys.OrderBy(static k => k, StringComparer.Ordinal);

        var y = 0;
        foreach (var key in ordered)
        {
            if (filter.Length > 0 && !key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var row = new StoreRow(key, () => CopyKey(key), () => EditKey(key), () => DeleteKey(key))
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

    private static Button MakeButton(string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, X = x, Y = y, Width = width };
        button.Accepted += (_, _) => action();
        return button;
    }

    private sealed class StoreRow : View
    {
        public StoreRow(string key, Action onCopy, Action onUpdate, Action onDelete)
        {
            Width = Dim.Fill();
            Height = 1;

            var label = new Label { Text = $" {key}", X = 0, Y = 0, Width = Dim.Fill(36) };

            var copy = new Button { Text = "Copy", X = Pos.AnchorEnd(33), Y = 0, Width = 10 };
            var update = new Button { Text = "Update", X = Pos.AnchorEnd(22), Y = 0, Width = 10 };
            var delete = new Button { Text = "Delete", X = Pos.AnchorEnd(11), Y = 0, Width = 10 };

            copy.Accepted += (_, _) => onCopy();
            update.Accepted += (_, _) => onUpdate();
            delete.Accepted += (_, _) => onDelete();

            Add(label, copy, update, delete);
        }
    }
}
