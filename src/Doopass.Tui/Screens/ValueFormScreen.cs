using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A single-value edit form (used for settings).</summary>
internal sealed class ValueFormScreen : ScreenBase
{
    private const int PanelWidth = 62;
    private const int PanelHeight = 9;
    private const int ActionWidth = 12;
    private const int ActionRow = 5;

    private readonly TextField _value;
    private readonly Action<string> _onSave;

    public ValueFormScreen(
        DoopassContext context,
        DoopassWindow shell,
        string title,
        string label,
        string value,
        Action<string> onSave) : base(context, shell)
    {
        _onSave = onSave;

        var panel = UiKit.Panel("edit setting", 0, 0, PanelWidth, PanelHeight);
        CenterView(panel, PanelWidth, PanelHeight);

        var caption = UiKit.Hint("  " + label, 0);
        _value = AddStop(UiKit.Input(1, text: value, width: 54));
        _value.X = 2;

        var save = AddStop(UiKit.Primary("Save", 0, ActionRow, () => _onSave(_value.Text ?? string.Empty), width: ActionWidth));
        var cancel = AddStop(UiKit.Primary("Cancel", 0, ActionRow, Shell.Back, width: ActionWidth));

        panel.Add(caption, _value, save, cancel);

        UiKit.CenterActions(panel, PanelWidth, ActionRow, 2, (save, ActionWidth), (cancel, ActionWidth));

        Add(panel, UiKit.Hint("  " + title, 10));

        _value.Accepted += (_, _) => _onSave(_value.Text ?? string.Empty);
    }

    public override string ScreenTitle => "edit setting";

    public override string FooterHint => "h/j/k/l move   enter save   esc cancel   q quit";
}
