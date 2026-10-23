using Doopass.Core;
using Doopass.Tui.Theme;
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
    private readonly Label _searchCaption;
    private readonly Label _searchPipe;
    private readonly Label _searchEnd;
    private List<string> _keys = new();
    private bool _revealed;

    public StoreScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        // Inline filter row: "search | field |" - no frame, matching the old UI.
        var searchCaption = new Label
        {
            Text = "search",
            X = 0,
            Y = 1,
            Width = 7,
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        var searchPipe = new Label
        {
            Text = "|",
            X = 7,
            Y = 1,
            Width = 1,
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        var searchEnd = new Label
        {
            Text = "|",
            X = 50,
            Y = 1,
            Width = 1,
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        _search = AddStop(UiKit.PlainInput(9, 1, 40));

        // ValueChanged is not raised per keystroke on TextField; TextChanging is.
        _search.TextChanging += (_, e) => ApplyFilter(e.Result ?? string.Empty);

        _searchCaption = searchCaption;
        _searchPipe = searchPipe;
        _searchEnd = searchEnd;

        _status = UiKit.Hint(string.Empty, 3);
        _reveal = UiKit.Hint("hidden", 1);
        _reveal.X = Pos.AnchorEnd(9);
        _reveal.Width = 8;

        var listPanel = UiKit.Panel("secrets", 0, 5, Dim.Fill(46), Dim.Fill(4));
        _list = AddStop(UiKit.List([]));
        _list.Accepted += (_, _) => CopySelected();
        _list.ValueChanged += (_, _) => ShowSelected();
        listPanel.Add(_list);

        var detailPanel = UiKit.Panel("details", Pos.AnchorEnd(45), 5, 45, Dim.Fill(4));
        _detail = UiKit.Detail(0, 0, Dim.Fill(), Dim.Fill());
        detailPanel.Add(_detail);

        Add(searchCaption, searchPipe, _search, searchEnd, _status, _reveal, listPanel, detailPanel);

        ApplyFilter(string.Empty);
    }

    public override string ScreenTitle => "store";

    public override void OnFocusMoved(View? focused)
    {

        var scheme = ReferenceEquals(focused, _search) ? Monochrome.ChromeSchemeName : Monochrome.DimSchemeName;

        foreach (var label in new[] { _searchCaption, _searchPipe, _searchEnd })
        {
            label.SchemeName = scheme;
            label.SetNeedsDraw();
        }
    }

    public override string FooterHint =>
        "h/j/k/l move   enter copy   v reveal   e edit   d delete   a add   g logout   esc back";

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
