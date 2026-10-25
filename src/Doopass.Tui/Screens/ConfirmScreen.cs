using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome yes/no confirmation.</summary>
internal sealed class ConfirmScreen : ScreenBase
{
    private const int PanelWidth = 60;
    private const int PanelHeight = 10;
    private const int ActionWidth = 8;
    private const int ActionRow = 6;

    public ConfirmScreen(DoopassContext context, DoopassWindow shell, string message, Action onYes)
        : base(context, shell)
    {
        var panel = UiKit.Panel("confirm", 0, 0, PanelWidth, PanelHeight);
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

        var no = AddStop(UiKit.Primary("No", 0, ActionRow, Shell.Back, width: ActionWidth));
        var yes = AddStop(UiKit.Primary("Yes", 0, ActionRow, onYes, width: ActionWidth));

        panel.Add(no, yes);

        // Centre the pair under the question instead of leaving it in the panel's left corner.
        UiKit.CenterActions(panel, PanelWidth, ActionRow, 2, (no, ActionWidth), (yes, ActionWidth));

        Add(panel);
    }

    public override string ScreenTitle => "confirm";

    public override string FooterHint => "enter yes   esc cancel   q quit";
}
