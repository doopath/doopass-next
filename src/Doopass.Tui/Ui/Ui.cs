using Doopass.Tui.Theme;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Ui;

/// <summary>
/// Visual building blocks plus an explicit focus order.
/// Terminal.Gui's <c>AdvanceFocus</c> does not descend into nested containers, so every
/// screen registers its controls here in visual order and navigation walks this list.
/// </summary>
internal static class UiKit
{
    public const int TileWidth = 46;
    public const int TileHeight = 3;
    public const int SmallTileWidth = 12;

    private static readonly Dictionary<View, List<View>> Screens = new();
    private static List<View> _active = new();

    public static void BeginScreen(View screen)
    {
        _active = new List<View>();
        Screens[screen] = _active;
    }

    public static void ActivateScreen(View screen)
    {
        if (Screens.TryGetValue(screen, out var list))
        {
            _active = list;
        }
    }

    public static void RegisterFocusable(View view) => _active.Add(view);

    /// <summary>Number of controls currently registered on the active screen.</summary>
    public static int RegisteredCount() => _active.Count;

    /// <summary>Drops dynamically rebuilt controls (list rows) from the focus order.</summary>
    public static void TrimRegistered(int count)
    {
        if (_active.Count > count)
        {
            _active.RemoveRange(count, _active.Count - count);
        }
    }

    public static void FocusInitial() => _active.FirstOrDefault()?.SetFocus();

    public static void AdvanceFocus(IApplication? app, bool forward)
    {
        if (app is null || _active.Count == 0)
        {
            return;
        }

        var current = app.Navigation?.GetFocused();
        var index = current is null ? -1 : _active.IndexOf(current);

        var next = forward
            ? (index < 0 ? 0 : (index + 1) % _active.Count)
            : (index <= 0 ? _active.Count - 1 : index - 1);

        _active[next].SetFocus();
    }

    /// <summary>A wide bordered tile (3 rows tall).</summary>
    public static Button Tile(string text, Pos x, int y, Action action, int width = TileWidth)
    {
        var button = new Button
        {
            Text = text,
            X = x,
            Y = y,
            Width = width,
            Height = TileHeight,
            ShadowStyle = null,
            SchemeName = Monochrome.SchemeName,
        };

        button.Accepted += (_, _) => action();
        RegisterFocusable(button);
        return button;
    }

    /// <summary>A compact bordered tile used inside list rows and toolbars.</summary>
    public static Button SmallTile(string text, Pos x, int y, Action action, int width = SmallTileWidth)
        => Tile(text, x, y, action, width);

    public static Label Title(string text, int y = 0) => new()
    {
        Text = text,
        X = Pos.Center(),
        Y = y,
        Width = Dim.Auto(),
        Height = 1,
        SchemeName = Monochrome.SchemeName,
    };

    public static Label Line(string text, int y, bool dim = false) => new()
    {
        Text = text,
        X = Pos.Center(),
        Y = y,
        Width = Dim.Auto(),
        Height = 1,
        SchemeName = dim ? Monochrome.DimSchemeName : Monochrome.SchemeName,
    };

    public static Label FieldLabel(string text, int y, int fieldWidth = 44) => new()
    {
        Text = text,
        X = Pos.Center() - (fieldWidth / 2),
        Y = y,
        Width = Dim.Auto(),
        Height = 1,
        SchemeName = Monochrome.DimSchemeName,
    };

    public static TextField Input(int y, bool secret = false, string text = "", int width = 44)
    {
        var field = new TextField
        {
            Secret = secret,
            Text = text,
            X = Pos.Center(),
            Y = y,
            Width = width,
            Height = 3,
            BorderStyle = LineStyle.Rounded,
            SchemeName = Monochrome.SchemeName,
        };

        RegisterFocusable(field);
        return field;
    }
}
