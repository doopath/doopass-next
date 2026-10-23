using Doopass.Tui.Ui;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome yes/no confirmation.</summary>
internal sealed class ConfirmScreen : ScreenBase
{
    public ConfirmScreen(DoopassContext context, DoopassWindow shell, string message, Action onYes)
        : base(context, shell)
    {
        var title = UiKit.Title("Confirm", 2);
        var text = UiKit.Line(message, 5);
        var no = UiKit.MenuButton("No", 9, Shell.Back);
        var yes = UiKit.MenuButton("Yes", 11, onYes);

        Add(title, text, no, yes);
    }
}
