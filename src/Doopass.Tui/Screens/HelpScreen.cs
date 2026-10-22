using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The help screen (hotkeys + about). Press Space/Back to leave.</summary>
internal sealed class HelpScreen : ScreenBase
{
    public HelpScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = new Label { Text = "Help Screen", X = Pos.Center(), Y = 1 };

        int y = 3;

        void Line(string text, int indent = 4)
        {
            Add(new Label { Text = new string(' ', indent) + text, X = 0, Y = y, Width = Dim.Fill() });
            y += 1;
        }

        Line("> Hotkeys <", 2); y += 0;
        Line("Q, ESCAPE    - quit the app");
        Line("H, F1        - open/close help screen");
        Line("SPACE        - leave help screen");
        Line("DownArrow, TAB, J - focus next element");
        Line("UpArrow, Shift+TAB, K - focus previous element");

        y += 1;
        Line("> About Doopass <", 2);
        Line("This software is ABSOLUTELY FREE and open-source.");
        Line("See LICENSE file for more information.");
        Line("Github:     https://github.com/doopath/PasswordManager");
        Line("Contact me: doopath@gmail.com");
        Line("Contact me: https://t.me/doopath");

        var back = new Button { Text = "Back (Space)", X = Pos.Center(), Y = y + 1, Width = 20 };
        back.Accepted += (_, _) => Shell.Back();

        Add(title, back);
    }
}
