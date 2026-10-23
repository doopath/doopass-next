using Doopass.Tui.Ui;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A reusable key/value entry form (add pair, update pair).</summary>
internal sealed class PairFormScreen : ScreenBase
{
    private readonly TextField _key;
    private readonly TextField _value;

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
        var title = UiKit.Title(buttonText, 1);
        var keyLabelView = UiKit.FieldLabel(keyLabel, 4);
        _key = UiKit.Input(5, text: key);
        var valueLabelView = UiKit.FieldLabel(valueLabel, 10);
        _value = UiKit.Input(11, text: value);

        var save = UiKit.MenuButton(buttonText, 16, () => onSave(_key.Text ?? string.Empty, _value.Text ?? string.Empty));
        var back = UiKit.MenuButton("Back", 18, Shell.Back);

        Add(title, keyLabelView, _key, valueLabelView, _value, save, back);

        _value.Accepted += (_, _) => onSave(_key.Text ?? string.Empty, _value.Text ?? string.Empty);
    }
}
