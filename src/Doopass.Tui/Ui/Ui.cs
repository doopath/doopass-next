using System.Collections.ObjectModel;
using Doopass.Tui.Theme;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Ui;

/// <summary>
/// Stateless factory for the visual building blocks shared by all screens.
/// Focus order is owned by each screen (see ScreenBase), never by a static registry.
/// </summary>
internal static class UiKit
{
    /// <summary>Rows reserved above the screen content (header bar).</summary>
    public const int ContentTop = 2;

    /// <summary>Rows reserved below the screen content (footer bar + gap).</summary>
    public const int FooterRows = 2;

    /// <summary>Width of the list column on the store screen.</summary>
    public const int ListWidth = 38;

    public static Label Hint(string text, Pos y) => new()
    {
        Text = text,
        X = 0,
        Y = y,
        Width = Dim.Fill(),
        Height = 1,
        SchemeName = Monochrome.DimSchemeName,
    };

    public static TextField Input(int y, bool secret = false, string text = "", int width = 44) => new()
    {
        Secret = secret,
        Text = text,
        X = 0,
        Y = y,
        Width = width,
        Height = 3,
        BorderStyle = LineStyle.Rounded,
        SchemeName = Monochrome.SchemeName,
    };

    /// <summary>A bare single-line input, used inline (e.g. the store filter row).</summary>
    public static TextField PlainInput(Pos x, Pos y, int width, string text = "") => new()
    {
        Text = text,
        X = x,
        Y = y,
        Width = width,
        Height = 1,
        SchemeName = Monochrome.SchemeName,
    };

    /// <summary>
    /// A flat, one-line action item. Terminal.Gui's Button draws decorative glyph
    /// boxes that do not fit a dense monochrome layout, so actions are focusable
    /// labels: the focused one simply inverts.
    /// </summary>
    public static Label Primary(string text, Pos x, Pos y, Action action, int width = 0)
    {
        var label = new Label
        {
            Text = "[ " + text + " ]",
            X = x,
            Y = y,
            Width = width > 0 ? width : text.Length + 4,
            Height = 1,
            CanFocus = true,
            SchemeName = Monochrome.SchemeName,
        };

        // Enter is already bound to Command.Accept by View; add Space as well.
        label.KeyBindings.ReplaceCommands(Key.Space, [Command.Accept]);
        label.Accepted += (_, _) => action();

        return label;
    }

    /// <summary>
    /// A menu row: a focusable, full-width label. Used where a ListView's internal
    /// horizontal scrolling would clip long, centered rows.
    /// </summary>
    public static Label MenuItem(string text, Pos x, Pos y, Action action, int width)
    {
        var label = new Label
        {
            Text = "  " + text,
            X = x,
            Y = y,
            Width = width,
            Height = 1,
            CanFocus = true,
            SchemeName = Monochrome.SchemeName,
        };

        label.KeyBindings.ReplaceCommands(Key.Space, [Command.Accept]);
        label.Accepted += (_, _) => action();

        return label;
    }

    /// <summary>A framed panel; children position themselves relative to the frame body.</summary>
    public static FrameView Panel(string title, Pos x, Pos y, Dim width, Dim height) => new()
    {
        Title = " " + title + " ",
        X = x,
        Y = y,
        Width = width,
        Height = height,
        BorderStyle = LineStyle.Rounded,
        SchemeName = Monochrome.SchemeName,
    };

    /// <summary>
    /// A scrolling list. Arrow keys, PgUp/PgDn and Home/End are handled natively
    /// by ListView; J/K are bound per instance so they never collide with the
    /// global key router.
    /// </summary>
    public static ListView List(IEnumerable<string> items)
    {
        var list = new ListView
        {
            Source = new ListWrapper<string>(new ObservableCollection<string>(items.ToList())),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            SchemeName = Monochrome.SchemeName,
        };

        list.KeyBindings.ReplaceCommands(Key.J, [Command.Down]);
        list.KeyBindings.ReplaceCommands(Key.K, [Command.Up]);
        list.KeyBindings.ReplaceCommands(Key.Enter, [Command.Accept]);
        list.SelectedItem = list.Source?.Count > 0 ? 0 : null;

        return list;
    }

    public static void SetItems(ListView list, IReadOnlyList<string> items, int select = 0)
    {
        list.Source = new ListWrapper<string>(new ObservableCollection<string>(items.ToList()));
        list.SelectedItem = items.Count == 0 ? null : Math.Clamp(select, 0, items.Count - 1);
        list.SetNeedsDraw();
    }

    /// <summary>Read-only, wrapped, scrollable text pane (the secret detail view).</summary>
    public static TextView Detail(Pos x, Pos y, Dim width, Dim height) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
        ReadOnly = true,
        WordWrap = true,
        ScrollBars = true,
        CanFocus = false,
        SchemeName = Monochrome.SchemeName,
    };

    /// <summary>Text for an empty list.</summary>
    public static string Empty(string text) => "  " + text;
}
