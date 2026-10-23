using Doopass.Tui.Theme;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Ui;

/// <summary>Small factories that keep every screen visually consistent.</summary>
internal static class UiKit
{
    public const int WideButtonWidth = 40;

    /// <summary>A full-width, borderless menu entry (inverts when focused).</summary>
    public static Button MenuButton(string text, int y, Action action, int width = WideButtonWidth)
    {
        var button = new Button
        {
            Text = text,
            X = Pos.Center(),
            Y = y,
            Width = width,
            Height = 1,
            ShadowStyle = null,
            SchemeName = Monochrome.SchemeName,
        };

        button.Accepted += (_, _) => action();
        return button;
    }

    /// <summary>A compact borderless button used inside toolbars and rows.</summary>
    public static Button InlineButton(string text, Pos x, int y, int width, Action action)
    {
        var button = new Button
        {
            Text = text,
            X = x,
            Y = y,
            Width = width,
            Height = 1,
            ShadowStyle = null,
            SchemeName = Monochrome.SchemeName,
        };

        button.Accepted += (_, _) => action();
        return button;
    }

    /// <summary>A centered, bold-ish screen title.</summary>
    public static Label Title(string text, int y = 1) => new()
    {
        Text = text,
        X = Pos.Center(),
        Y = y,
        Width = Dim.Auto(),
        Height = 1,
        SchemeName = Monochrome.SchemeName,
    };

    /// <summary>A centered single-line label; dim for secondary information.</summary>
    public static Label Line(string text, int y, bool dim = false) => new()
    {
        Text = text,
        X = Pos.Center(),
        Y = y,
        Width = Dim.Auto(),
        Height = 1,
        SchemeName = dim ? Monochrome.DimSchemeName : Monochrome.SchemeName,
    };

    /// <summary>A left-aligned label, aligned to a centered form column.</summary>
    public static Label FieldLabel(string text, int y, int fieldWidth = 44) => new()
    {
        Text = text,
        X = Pos.Center() - (fieldWidth / 2),
        Y = y,
        Width = Dim.Auto(),
        Height = 1,
        SchemeName = Monochrome.DimSchemeName,
    };

    /// <summary>A bordered, single-line text input (height 3 including the frame).</summary>
    public static TextField Input(int y, bool secret = false, string text = "", int width = 44) => new()
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
}
