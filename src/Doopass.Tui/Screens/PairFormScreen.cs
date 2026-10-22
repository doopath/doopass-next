using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A reusable key/value entry form (add pair, update pair, edit setting).</summary>
internal sealed class PairFormScreen : ScreenBase
{
    private readonly TextField _keyField;
    private readonly TextField _valueField;
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

        var keyTitle = new Label { Text = $"{keyLabel}:", X = Pos.Center() - 22, Y = 3, Width = 12 };
        _keyField = new TextField { Text = key, X = Pos.Center() - 8, Y = 3, Width = 40 };

        var valueTitle = new Label { Text = $"{valueLabel}:", X = Pos.Center() - 22, Y = 6, Width = 12 };
        _valueField = new TextField { Text = value, X = Pos.Center() - 8, Y = 6, Width = 40 };

        var save = MakeButton(buttonText, 9, () =>
        {
            _onSave(_keyField.Text ?? string.Empty, _valueField.Text ?? string.Empty);
        });

        var back = MakeButton("Back", 11, Shell.Back);

        Add(keyTitle, _keyField, valueTitle, _valueField, save, back);
    }

    private static Button MakeButton(string text, int y, Action action)
    {
        var button = new Button { Text = text, X = Pos.Center(), Y = y, Width = 24 };
        button.Accepted += (_, _) => action();
        return button;
    }
}
