using Doopass.Tui.Ui;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome message screen (replaces the colored MessageBox).</summary>
internal sealed class MessageScreen : ScreenBase
{
    public MessageScreen(DoopassContext context, DoopassWindow shell, string message, Action onOk)
        : base(context, shell)
    {
        var title = UiKit.Title("Notice", 2);
        var text = UiKit.Line(message, 5);
        var ok = UiKit.MenuButton("OK", 9, onOk);

        Add(title, text, ok);
    }
}
