using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
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
        var title = UiKit.Title(buttonText, 0);
        var keyLabelView = UiKit.FieldLabel(keyLabel, 3);
        _key = UiKit.Input(4, text: key);
        var valueLabelView = UiKit.FieldLabel(valueLabel, 8);
        _value = UiKit.Input(9, text: value);

        var save = UiKit.Tile(buttonText, Pos.Center(), 13, () => onSave(_key.Text ?? string.Empty, _value.Text ?? string.Empty));
        var back = UiKit.Tile("Back", Pos.Center(), 16, Shell.Back);

        Add(title, keyLabelView, _key, valueLabelView, _value, save, back);

        _value.Accepted += (_, _) => onSave(_key.Text ?? string.Empty, _value.Text ?? string.Empty);
    }
}
