using Doopass.Core;
using Doopass.Tui.Screens;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui;

/// <summary>
/// The root window. Owns a simple screen stack (mirroring the original
/// push/pop navigation) and handles the global hotkeys.
/// </summary>
internal sealed class DoopassWindow : Window
{
    private readonly DoopassContext _context;
    private readonly View _content;
    private readonly Stack<ScreenBase> _stack = new();

    public DoopassWindow(DoopassContext context)
    {
        _context = context;

        Title = "Doopass — Password Manager";

        var header = new Label
        {
            Text = " Doopass — Password Manager",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        var footer = new Label
        {
            Text = " F1/H Help   Tab/Shift+Tab Next/Prev   J/K/↑/↓ Focus   Q/Esc Quit ",
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };

        _content = new View
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        Add(header, _content, footer);

        _stack.Push(new MainMenuScreen(_context, this));
        _content.Add(_stack.Peek());
    }

    public DoopassContext Context => _context;

    private ScreenBase? Top => _stack.Count > 0 ? _stack.Peek() : null;

    /// <summary>Replaces the current screen with a new one.</summary>
    public void Replace(ScreenBase screen)
    {
        if (_stack.Count > 0)
        {
            _stack.Pop();
        }

        _stack.Push(screen);
        Render();
    }

    /// <summary>Pushes a new screen on top of the current one.</summary>
    public void Push(ScreenBase screen)
    {
        _stack.Push(screen);
        Render();
    }

    /// <summary>Returns to the previous screen.</summary>
    public void Back()
    {
        if (_stack.Count <= 1)
        {
            return;
        }

        _stack.Pop();
        Render();
    }

    /// <summary>Clears the stack and returns to the main menu.</summary>
    public void GotoMain()
    {
        _stack.Clear();
        _stack.Push(new MainMenuScreen(_context, this));
        Render();
    }

    /// <summary>Exits the application.</summary>
    public void Quit() => App?.RequestStop();

    private void Render()
    {
        foreach (var view in _content.SubViews.ToArray())
        {
            _content.Remove(view);
            view.Dispose();
        }

        if (Top is { } top)
        {
            _content.Add(top);
            top.FocusInitial();
        }

        App?.LayoutAndDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        var navigation = App?.Navigation;
        var focused = navigation?.GetFocused();
        var isTyping = focused is TextField;

        if (key.KeyCode == KeyCode.F1)
        {
            ToggleHelp();
            return true;
        }

        if (key.KeyCode == KeyCode.Esc)
        {
            Quit();
            return true;
        }

        if (isTyping)
        {
            return base.OnKeyDown(key);
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

        if (key.KeyCode == KeyCode.J || key.KeyCode == KeyCode.CursorDown)
        {
            navigation?.AdvanceFocus(NavigationDirection.Forward, null);
            return true;
        }

        if (key.KeyCode == KeyCode.K || key.KeyCode == KeyCode.CursorUp)
        {
            navigation?.AdvanceFocus(NavigationDirection.Backward, null);
            return true;
        }

        return base.OnKeyDown(key);
    }

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
