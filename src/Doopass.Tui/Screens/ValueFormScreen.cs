using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>A single-value edit form (used for settings).</summary>
internal sealed class ValueFormScreen : ScreenBase
{
    private readonly TextField _value;

    public ValueFormScreen(
        DoopassContext context,
        DoopassWindow shell,
        string title,
        string label,
        string value,
        Action<string> onSave) : base(context, shell)
    {
        var titleView = UiKit.Title(title, 0);
        var labelView = UiKit.FieldLabel(label, 3);
        _value = UiKit.Input(4, text: value);

        var save = UiKit.Tile("Save", Pos.Center(), 8, () => onSave(_value.Text ?? string.Empty));
        var back = UiKit.Tile("Back", Pos.Center(), 11, Shell.Back);

        Add(titleView, labelView, _value, save, back);

        _value.Accepted += (_, _) => onSave(_value.Text ?? string.Empty);
    }
}
