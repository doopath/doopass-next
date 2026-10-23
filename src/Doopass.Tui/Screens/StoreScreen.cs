using Doopass.Core;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>
/// The store screen: a search box, a scrolling list of keys and a details pane
/// for the selected secret. Actions live on hotkeys; Tab only switches between
/// search and the list.
/// </summary>
internal sealed class StoreScreen : ScreenBase
{
    private readonly TextField _search;
    private readonly ListView _list;
    private readonly TextView _detail;
    private readonly Label _status;
    private readonly Label _reveal;
    private List<string> _keys = new();
    private bool _revealed;

    public StoreScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        // The search field lives in its own framed panel, so its caption looks and
        // behaves exactly like the "secrets" panel title.
        var searchPanel = UiKit.Panel("search", 0, 0, 46, 6);
        _search = AddStop(UiKit.Input(1, width: 42));
        _search.X = 2;
        searchPanel.Add(_search);

        _status = UiKit.Hint(string.Empty, 6);
        _reveal = UiKit.Hint("hidden", 1);
        _reveal.X = Pos.AnchorEnd(9);
        _reveal.Width = 8;

        var listPanel = UiKit.Panel("secrets", 0, 8, Dim.Fill(46), Dim.Fill(1));
        _list = AddStop(UiKit.List([]));
        _list.Accepted += (_, _) => CopySelected();
        _list.ValueChanged += (_, _) => ShowSelected();
        listPanel.Add(_list);

        var detailPanel = UiKit.Panel("details", Pos.AnchorEnd(45), 8, 45, Dim.Fill(1));
        _detail = UiKit.Detail(0, 0, Dim.Fill(), Dim.Fill());
        detailPanel.Add(_detail);

        Add(searchPanel, _status, _reveal, listPanel, detailPanel);

        _search.ValueChanged += (_, _) => ApplyFilter(_search.Text ?? string.Empty);
        _search.Accepted += (_, _) => ApplyFilter(_search.Text ?? string.Empty);

        ApplyFilter(string.Empty);
    }

    public override string ScreenTitle => "store";

    public override string FooterHint =>
        "h/j/k/l move   enter copy   v reveal   e edit   d delete   a add   ч logout   esc back";

    /// <summary>Hotkeys routed from <see cref="DoopassWindow"/> while the list has focus.</summary>
    public void RunShortcut(char shortcut)
    {
        switch (shortcut)
        {
            case 'v':
                ToggleReveal();
                break;
            case 'e':
                BeginEdit(SelectedKey);
                break;
            case 'd':
                ConfirmDelete(SelectedKey);
                break;
            case 'a':
                BeginAdd();
                break;
        }
    }

    private string? SelectedKey =>
        _list.SelectedItem is { } index && index >= 0 && index < _keys.Count ? _keys[index] : null;

    private void ApplyFilter(string filter)
    {
        var previous = SelectedKey;

        _keys = (Context.CurrentStore?.GetKeys() ?? [])
            .Where(key => filter.Length == 0 || key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var select = previous is null ? 0 : _keys.IndexOf(previous);
        UiKit.SetItems(_list, _keys.Select(RowText).ToList(), Math.Max(0, select));

        _status.Text = _keys.Count == 0
            ? (filter.Length == 0 ? "  no items yet - press a to add a pair" : "  no matches")
            : $"  {_keys.Count} item{(_keys.Count == 1 ? string.Empty : "s")}";

        ShowSelected();
    }

    private static string RowText(string key)
    {
        var shown = key.Length <= 30 ? key : key[..29] + "…";
        return " " + shown.PadRight(32) + "••••";
    }

    private void ShowSelected()
    {
        if (SelectedKey is not { } key)
        {
            _detail.Text = "\n  no selection\n\n  press [ a ] to add a pair";
            _detail.SetNeedsDraw();
            return;
        }

        var value = Context.CurrentStore?.GetValue(key) ?? string.Empty;
        var body = _revealed ? value : new string('•', Math.Min(value.Length, 40));

        _detail.Text =
            $"\n  KEY\n  {key}\n\n  VALUE  ({value.Length} chars)\n  {body}";

        _reveal.Text = _revealed ? "visible" : "hidden";
        _detail.SetNeedsDraw();
        _reveal.SetNeedsDraw();
    }

    private void ToggleReveal()
    {
        _revealed = !_revealed;
        ShowSelected();
    }

    private void CopySelected()
    {
        if (SelectedKey is not { } key)
        {
            return;
        }

        var value = Context.CurrentStore?.GetValue(key) ?? string.Empty;
        _ = App?.Clipboard?.TrySetClipboardData(value);
        _status.Text = $"  copied '{key}' ({value.Length} chars) to clipboard";
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

    private void BeginEdit(string? key)
    {
        if (key is null)
        {
            return;
        }

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

    private void ConfirmDelete(string? key)
    {
        if (key is null)
        {
            return;
        }

        Shell.Confirm($"Delete '{key}'?", () =>
        {
            Context.CurrentStore!.RemoveProperty(key);
            Shell.CloseAndReplace(new StoreScreen(Context, Shell));
        });
    }
}
