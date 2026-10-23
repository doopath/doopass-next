using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Doopass.Tui.Ui;

/// <summary>What a global key press means, independent of Terminal.Gui.</summary>
internal enum TuiAction
{
    None,
    FocusNext,
    FocusPrevious,
    Back,
    Quit,
    Help,
}

/// <summary>Everything the mapping needs to know about the current focus.</summary>
internal readonly record struct KeyRouteContext(bool IsTextInput, bool IsList, bool CanGoBack);

/// <summary>
/// Pure key mapping. Global hotkeys are resolved here so they can be unit tested
/// without an application, a terminal or a timer.
/// </summary>
internal static class TuiKeyMap
{
    /// <summary>Actions available on the store screen when the list has no focus.</summary>
    public static TuiAction Resolve(Key key, KeyRouteContext ctx)
    {
        if (Is(key, Key.Tab))
        {
            return key.IsShift ? TuiAction.FocusPrevious : TuiAction.FocusNext;
        }

        if (Is(key, Key.Esc))
        {
            return ctx.CanGoBack ? TuiAction.Back : TuiAction.Quit;
        }

        if (Is(key, Key.F1))
        {
            return TuiAction.Help;
        }

        // Horizontal movement between regions (list <-> details, field <-> buttons).
        // Handled before the text-input guard so a focused text pane can be left.
        if (Is(key, Key.H))
        {
            return TuiAction.FocusPrevious;
        }

        if (Is(key, Key.L))
        {
            return TuiAction.FocusNext;
        }

        // Everything below would steal keystrokes from an editor.
        if (ctx.IsTextInput)
        {
            return TuiAction.None;
        }

        if (Is(key, Key.Q))
        {
            return TuiAction.Quit;
        }

        // A ListView owns its own arrows and its own J/K bindings.
        if (ctx.IsList)
        {
            return TuiAction.None;
        }

        if (Is(key, Key.CursorDown) || Is(key, Key.J))
        {
            return TuiAction.FocusNext;
        }

        if (Is(key, Key.CursorUp) || Is(key, Key.K))
        {
            return TuiAction.FocusPrevious;
        }

        return TuiAction.None;
    }

    // Compare KeyCode with modifiers stripped: a Key carries shift/alt/ctrl bits,
    // so a plain == against e.g. Key.Tab would fail for Shift+Tab.
    private const KeyCode Modifiers = KeyCode.ShiftMask | KeyCode.AltMask | KeyCode.CtrlMask;

    private static bool Is(Key key, Key keyToMatch) => (key.KeyCode & ~Modifiers) == keyToMatch.KeyCode;
}
