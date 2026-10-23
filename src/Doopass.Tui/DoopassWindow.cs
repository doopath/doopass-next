using Doopass.Core;
using Doopass.Tui.Screens;
using Doopass.Tui.Theme;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui;

/// <summary>
/// The root window. Owns a screen stack (mirroring the original push/pop
/// navigation) and handles the global hotkeys.
/// </summary>
internal sealed class DoopassWindow : Window
{
    private readonly DoopassContext _context;
    private readonly View _content;
    private readonly Stack<ScreenBase> _stack = new();
    private ScreenBase? _current;

    public DoopassWindow(DoopassContext context)
    {
        _context = context;

        Title = "Doopass";
        BorderStyle = LineStyle.None;
        ShadowStyle = null;
        SchemeName = Monochrome.SchemeName;

        _content = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = Monochrome.SchemeName,
        };

        var hints = new Label
        {
            Text = "  Tab/Up/Down/J/K move    Enter select    H help    Q quit",
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        var version = new Label
        {
            Text = $"v{Constants.Version}",
            X = Pos.AnchorEnd(10),
            Y = Pos.AnchorEnd(1),
            Width = 9,
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        Add(_content, hints, version);

        _stack.Push(new MainMenuScreen(_context, this));
        ShowCurrent();
    }

    public DoopassContext Context => _context;

    private ScreenBase? Top => _stack.Count > 0 ? _stack.Peek() : null;

    /// <summary>Replaces the current screen (the previous one is disposed).</summary>
    public void Replace(ScreenBase screen)
    {
        DetachCurrent();

        if (_stack.Count > 0)
        {
            _stack.Pop().Dispose();
        }

        _stack.Push(screen);
        ShowCurrent();
    }

    /// <summary>Pushes a screen on top; the previous screen stays alive for <see cref="Back"/>.</summary>
    public void Push(ScreenBase screen)
    {
        DetachCurrent();
        _stack.Push(screen);
        ShowCurrent();
    }

    /// <summary>Returns to the previous screen.</summary>
    public void Back()
    {
        if (_stack.Count <= 1)
        {
            return;
        }

        DetachCurrent();
        _stack.Pop().Dispose();
        ShowCurrent();
    }

    /// <summary>
    /// Closes the current screen and the one it was opened from, then shows
    /// <paramref name="screen"/>. Used after a dialog/confirm to refresh a parent.
    /// </summary>
    public void CloseAndReplace(ScreenBase screen)
    {
        DetachCurrent();

        if (_stack.Count > 0)
        {
            _stack.Pop().Dispose();
        }

        if (_stack.Count > 0)
        {
            _stack.Pop().Dispose();
        }

        _stack.Push(screen);
        ShowCurrent();
    }

    /// <summary>Clears the stack and returns to the main menu.</summary>
    public void GotoMain()
    {
        DetachCurrent();

        foreach (var screen in _stack)
        {
            screen.Dispose();
        }

        _stack.Clear();
        _stack.Push(new MainMenuScreen(_context, this));
        ShowCurrent();
    }

    /// <summary>Exits the application.</summary>
    public void Quit() => App?.RequestStop();

    /// <summary>Shows a monochrome message screen; the callback runs when OK is pressed.</summary>
    public void Message(string text, Action onOk) => Push(new MessageScreen(_context, this, text, onOk));

    /// <summary>Shows a yes/no confirmation; the callback runs when Yes is pressed.</summary>
    public void Confirm(string text, Action onYes) => Push(new ConfirmScreen(_context, this, text, onYes));

    private void DetachCurrent()
    {
        if (_current is null)
        {
            return;
        }

        _content.Remove(_current);
        _current = null;
    }

    private void ShowCurrent()
    {
        if (Top is not { } top)
        {
            return;
        }

        _current = top;
        _content.Add(top);

        App?.LayoutAndDraw();
        top.FocusInitial();
        App?.LayoutAndDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        var focused = App?.Navigation?.GetFocused();
        var isTyping = focused is TextField;

        if (key.KeyCode == KeyCode.F1)
        {
            ToggleHelp();
            return true;
        }

        if (key.KeyCode == KeyCode.Esc)
        {
            if (Top is HelpScreen or MessageScreen or ConfirmScreen)
            {
                Back();
            }
            else
            {
                Quit();
            }

            return true;
        }

        // Arrows always move focus, even out of a single-line text field.
        if (key.KeyCode == KeyCode.CursorDown)
        {
            AdvanceFocus(NavigationDirection.Forward);
            return true;
        }

        if (key.KeyCode == KeyCode.CursorUp)
        {
            AdvanceFocus(NavigationDirection.Backward);
            return true;
        }

        if (isTyping)
        {
            return base.OnKeyDown(key);
        }

        if (key.KeyCode == KeyCode.J)
        {
            AdvanceFocus(NavigationDirection.Forward);
            return true;
        }

        if (key.KeyCode == KeyCode.K)
        {
            AdvanceFocus(NavigationDirection.Backward);
            return true;
        }

        if (key.KeyCode == KeyCode.H)
        {
            ToggleHelp();
            return true;
        }

        if (key.KeyCode == KeyCode.Q)
        {
            Quit();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private void AdvanceFocus(NavigationDirection direction) =>
        App?.Navigation?.AdvanceFocus(direction, TabBehavior.TabStop);

    private void ToggleHelp()
    {
        if (Top is HelpScreen)
        {
            Back();
            return;
        }

        Push(new HelpScreen(_context, this));
    }
}
