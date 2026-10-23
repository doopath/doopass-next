using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A minimal monochrome yes/no confirmation.</summary>
internal sealed class ConfirmScreen : ScreenBase
{
    public ConfirmScreen(DoopassContext context, DoopassWindow shell, string message, Action onYes)
        : base(context, shell)
    {
        var panel = UiKit.Panel("confirm", 0, 0, 60, 10);
        CenterView(panel, 60, 10);

        panel.Add(new Label
        {
            Text = "  " + message,
            X = 0,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 2,
            SchemeName = Theme.Monochrome.SchemeName,
        });

        var no = AddStop(UiKit.Primary("No", 2, 6, Shell.Back, width: 8));
        var yes = AddStop(UiKit.Primary("Yes", 12, 6, onYes, width: 8));

        panel.Add(no, yes);

        Add(panel);
    }

    public override string ScreenTitle => "confirm";

    public override string FooterHint => "enter yes   esc cancel   q quit";
}
