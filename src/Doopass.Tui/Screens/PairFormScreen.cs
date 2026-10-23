using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A reusable key/value entry form (add pair, update pair).</summary>
internal sealed class PairFormScreen : ScreenBase
{
    private readonly TextField _key;
    private readonly TextField _value;
    private readonly Action<string, string> _onSave;

    public PairFormScreen(
        DoopassContext context,
        DoopassWindow shell,
        string keyLabel,
        string valueLabel,
        string key,
        string value,
        string buttonText,
        Action<string, string> onSave) : base(context, shell)
    {
        _onSave = onSave;

        var panel = UiKit.Panel(buttonText.ToLowerInvariant(), 0, 0, 62, 15);
        CenterView(panel, 62, 15);

        var keyCaption = UiKit.Hint("  " + keyLabel, 0);
        _key = AddStop(UiKit.Input(1, text: key, width: 54));
        _key.X = 2;

        var valueCaption = UiKit.Hint("  " + valueLabel, 5);
        _value = AddStop(UiKit.Input(6, text: value, width: 54));
        _value.X = 2;

        var saveWidth = Math.Max(12, buttonText.Length + 6);
        const int cancelWidth = 12;
        var save = AddStop(UiKit.Primary(buttonText, 2, 10, Submit, width: saveWidth));
        var cancel = AddStop(UiKit.Primary("Cancel", 2 + saveWidth + 2, 10, Shell.Back, width: cancelWidth));

        panel.Add(keyCaption, _key, valueCaption, _value, save, cancel);

        // Centre the button pair inside the panel once its width is known.
        panel.SubViewLayout += (_, _) =>
        {
            var total = saveWidth + 2 + cancelWidth;
            var start = Math.Max(1, (panel.Viewport.Width - total) / 2);
            save.X = start;
            cancel.X = start + saveWidth + 2;
        };

        Add(panel);

        _value.Accepted += (_, _) => Submit();
    }

    public override string ScreenTitle => "pair";

    public override string FooterHint => "h/j/k/l move   enter save   esc cancel   q quit";

    private void Submit() => _onSave(_key.Text ?? string.Empty, _value.Text ?? string.Empty);
}
