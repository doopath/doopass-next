using Doopass.Core;
using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The help screen (hotkeys and about). Dismiss with Back, Esc or F1.</summary>
internal sealed class HelpScreen : ScreenBase
{
    private static readonly (string Keys, string What)[] Hotkeys =
    [
        ("j / k  or  up / down", "move the selection, scroll the list"),
        ("h / l", "move focus left / right between regions"),
        ("page up / page down", "scroll a list or a value by a page"),
        ("home / end", "jump to the first / last item"),
        ("enter", "select, copy the secret, submit the form"),
        ("tab / shift+tab", "move focus between the list, fields and buttons"),
        ("v", "reveal or hide the selected value (store)"),
        ("e / d / a", "edit, delete or add a pair (store)"),
        ("c / r / d", "create, restore or delete a backup"),
        ("esc", "go back one screen"),
        ("q", "quit doopass"),
        ("f1", "open or close this help"),
    ];

    public HelpScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var panel = UiKit.Panel("keyboard", 0, 0, Dim.Fill(), Dim.Fill(3));

        var y = 0;

        foreach (var (keys, what) in Hotkeys)
        {
            panel.Add(new Label
            {
                Text = "  " + keys.PadRight(22) + what,
                X = 0,
                Y = y++,
                Width = Dim.Fill(),
                Height = 1,
                SchemeName = Monochrome.SchemeName,
            });
        }

        var back = AddStop(UiKit.Primary("Back", 0, Pos.AnchorEnd(3), Shell.Back, width: 10));

        Add(panel, back, UiKit.Hint($"  doopass {Constants.Version} · local encrypted password store", Pos.AnchorEnd(4)));
    }

    public override string ScreenTitle => "help";

    public override string FooterHint => "esc close   q quit";
}
