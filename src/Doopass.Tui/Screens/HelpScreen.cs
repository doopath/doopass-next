using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The help screen (hotkeys + about). Dismiss with Back or H.</summary>
internal sealed class HelpScreen : ScreenBase
{
    public HelpScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = UiKit.Title("Help", 0);

        var y = 2;

        void AddLine(string text, bool dim = false)
        {
            Add(new Label
            {
                Text = new string(' ', 4) + text,
                X = 0,
                Y = y,
                Width = Dim.Fill(),
                Height = 1,
                SchemeName = dim ? Monochrome.DimSchemeName : Monochrome.SchemeName,
            });
            y++;
        }

        AddLine("Hotkeys");
        AddLine("  Tab / Up / Down / J / K    move focus", dim: true);
        AddLine("  Enter                     select / submit", dim: true);
        AddLine("  H                         open or close this help", dim: true);
        AddLine("  Q / Esc                   quit", dim: true);
        y++;
        AddLine("About");
        AddLine("  Doopass is free and open-source.", dim: true);
        AddLine("  github.com/doopath/PasswordManager", dim: true);

        var back = UiKit.Tile("Back", Pos.Center(), y + 1, Shell.Back);

        Add(title, back);
    }
}
