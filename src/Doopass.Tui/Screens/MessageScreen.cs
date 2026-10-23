using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome message screen.</summary>
internal sealed class MessageScreen : ScreenBase
{
    public MessageScreen(DoopassContext context, DoopassWindow shell, string message, Action onOk)
        : base(context, shell)
    {
        var panel = UiKit.Panel("notice", 0, 0, 60, 9);
        CenterView(panel, 60, 9);

        panel.Add(new Label
        {
            Text = "  " + message,
            X = 0,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 2,
            SchemeName = Theme.Monochrome.SchemeName,
        });

        var ok = AddStop(UiKit.Primary("OK", 2, 4, onOk, width: 10));
        panel.Add(ok);

        Add(panel);
    }

    public override string ScreenTitle => "notice";

    public override string FooterHint => "enter ok   esc close   q quit";
}
