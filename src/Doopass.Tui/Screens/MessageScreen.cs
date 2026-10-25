using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome message screen.</summary>
internal sealed class MessageScreen : ScreenBase
{
    private const int PanelWidth = 60;
    private const int PanelHeight = 9;
    private const int ActionWidth = 10;
    private const int ActionRow = 4;

    public MessageScreen(DoopassContext context, DoopassWindow shell, string message, Action onOk)
        : base(context, shell)
    {
        var panel = UiKit.Panel("notice", 0, 0, PanelWidth, PanelHeight);
        CenterView(panel, PanelWidth, PanelHeight);

        panel.Add(new Label
        {
            Text = "  " + message,
            X = 0,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 2,
            SchemeName = Theme.Monochrome.SchemeName,
        });

        var ok = AddStop(UiKit.Primary("OK", 0, ActionRow, onOk, width: ActionWidth));
        panel.Add(ok);

        UiKit.CenterActions(panel, PanelWidth, ActionRow, 2, (ok, ActionWidth));

        Add(panel);
    }

    public override string ScreenTitle => "notice";

    public override string FooterHint => "enter ok   esc close   q quit";
}
