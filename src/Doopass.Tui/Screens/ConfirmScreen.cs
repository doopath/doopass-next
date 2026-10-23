using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome yes/no confirmation.</summary>
internal sealed class ConfirmScreen : ScreenBase
{
    public ConfirmScreen(DoopassContext context, DoopassWindow shell, string message, Action onYes)
        : base(context, shell)
    {
        var title = UiKit.Title("Confirm", 2);
        var text = UiKit.Line(message, 5);
        var no = UiKit.Tile("No", Pos.Center(), 9, Shell.Back);
        var yes = UiKit.Tile("Yes", Pos.Center(), 12, onYes);

        Add(title, text, no, yes);
    }
}
