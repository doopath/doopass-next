using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Doopass.Tui.Theme;

/// <summary>
/// A monochrome scheme: static black background, white foreground, and an
/// inverted (white-on-black) highlight for the focused element. Only the
/// secondary "dim" scheme uses gray, for hints and footers.
/// </summary>
internal static class Monochrome
{
    public const string SchemeName = "doopass";
    public const string DimSchemeName = "doopass-dim";

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        var normal = new Scheme
        {
            Normal = new GuiAttribute(StandardColor.White, StandardColor.Black),
            Focus = new GuiAttribute(StandardColor.Black, StandardColor.White),
            HotNormal = new GuiAttribute(StandardColor.White, StandardColor.Black, TextStyle.Underline),
            HotFocus = new GuiAttribute(StandardColor.Black, StandardColor.White, TextStyle.Underline),
            Active = new GuiAttribute(StandardColor.Black, StandardColor.White, TextStyle.Bold),
            Editable = new GuiAttribute(StandardColor.White, StandardColor.Black),
            ReadOnly = new GuiAttribute(StandardColor.Gray, StandardColor.Black),
            Disabled = new GuiAttribute(StandardColor.DarkGray, StandardColor.Black),
        };

        var dim = new Scheme
        {
            Normal = new GuiAttribute(StandardColor.DarkGray, StandardColor.Black),
            Focus = new GuiAttribute(StandardColor.Black, StandardColor.Gray),
            Editable = new GuiAttribute(StandardColor.DarkGray, StandardColor.Black),
            Disabled = new GuiAttribute(StandardColor.DarkGray, StandardColor.Black),
        };

        SchemeManager.AddScheme(SchemeName, normal);
        SchemeManager.AddScheme(DimSchemeName, dim);
    }
}
