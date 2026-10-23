using Doopass.Core;
using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The store screen: search plus a list of key tiles with actions.</summary>
internal sealed class StoreScreen : ScreenBase
{
    private const int RowHeight = 3;

    private readonly TextField _search;
    private readonly View _items;
    private readonly Label _count;
    private readonly Label _status;
    private readonly List<View> _rows = new();
    private readonly int _baseFocusCount;

    public StoreScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = UiKit.Title("Store", 0);
        _count = UiKit.Line(string.Empty, 1, dim: true);

        _search = UiKit.Input(3);

        var add = UiKit.Tile("Add pair", 1, 7, BeginAdd, width: 14);
        var back = UiKit.Tile("Back", 16, 7, Shell.GotoMain, width: 9);
        var exit = UiKit.Tile("Exit", 27, 7, Shell.Quit, width: 9);

        _status = new Label
        {
            Text = string.Empty,
            X = 1,
            Y = 11,
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        _items = new View
        {
            X = 0,
            Y = 12,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = Monochrome.SchemeName,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(title, _count, _search, add, back, exit, _status, _items);

        _baseFocusCount = UiKit.RegisteredCount();

        _search.Accepted += (_, _) => ApplyFilter(_search.Text ?? string.Empty);
        _search.ValueChanged += (_, _) => ApplyFilter(_search.Text ?? string.Empty);

        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string filter)
    {
        UiKit.TrimRegistered(_baseFocusCount);

        foreach (var row in _rows)
        {
            _items.Remove(row);
            row.Dispose();
        }

        _rows.Clear();

        var keys = (Context.CurrentStore?.GetKeys() ?? [])
            .Where(key => filter.Length == 0 || key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        _count.Text = $"{keys.Count} item{(keys.Count == 1 ? string.Empty : "s")}";

        if (keys.Count == 0)
        {
            _rows.Add(new Label
            {
                Text = filter.Length == 0 ? "no items yet - press Add pair" : "no matches",
                X = Pos.Center(),
                Y = 1,
                Width = Dim.Auto(),
                Height = 1,
                SchemeName = Monochrome.DimSchemeName,
            });

            _items.Add(_rows[0]);
        }
        else
        {
            var y = 0;

            foreach (var key in keys)
            {
                var row = new StoreRow(key, () => CopyKey(key), () => BeginEdit(key), () => ConfirmDelete(key))
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

    private void CopyKey(string key)
    {
        var value = Context.CurrentStore?.GetValue(key) ?? string.Empty;
        _ = App?.Clipboard?.TrySetClipboardData(value);
        _status.Text = $"copied '{key}'";
    }

    private void BeginAdd()
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
                    Shell.CloseAndReplace(new StoreScreen(Context, Shell));
                }
                catch (PropertyAlreadyExistError ex)
                {
                    Shell.Message(ex.Message, Shell.Back);
                }
            }));
    }

    private void BeginEdit(string key)
    {
        var store = Context.CurrentStore!;
        var value = store.GetValue(key);

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
                    if (!string.Equals(newKey, key, StringComparison.Ordinal) && store.DoesPropertyExist(newKey))
                    {
                        throw new PropertyAlreadyExistError($"This property ({newKey}) already exists!");
                    }

                    store.RemoveProperty(key);
                    store.AddProperty(newKey, newValue);
                    Shell.CloseAndReplace(new StoreScreen(Context, Shell));
                }
                catch (Exception ex) when (ex is PropertyDoesNotExistError or PropertyAlreadyExistError)
                {
                    Shell.Message(ex.Message, Shell.Back);
                }
            }));
    }

    private void ConfirmDelete(string key)
    {
        Shell.Confirm($"Delete '{key}'?", () =>
        {
            Context.CurrentStore!.RemoveProperty(key);
            Shell.CloseAndReplace(new StoreScreen(Context, Shell));
        });
    }

    private sealed class StoreRow : View
    {
        public StoreRow(string key, Action onCopy, Action onEdit, Action onDelete)
        {
            Width = Dim.Fill();
            Height = RowHeight;
            CanFocus = true;
            TabStop = TabBehavior.NoStop;
            SchemeName = Monochrome.SchemeName;

            var label = new Label
            {
                Text = key,
                X = 2,
                Y = 1,
                Width = Dim.Fill(26),
                Height = 1,
                SchemeName = Monochrome.SchemeName,
            };

            Add(
                label,
                UiKit.SmallTile("copy", Pos.AnchorEnd(25), 0, onCopy, width: 8),
                UiKit.SmallTile("edit", Pos.AnchorEnd(17), 0, onEdit, width: 8),
                UiKit.SmallTile("del", Pos.AnchorEnd(9), 0, onDelete, width: 8));
        }
    }
}
