using Doopass.Core;
using Doopass.Tui.Screens;
using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui;

/// <summary>
/// The root window: header bar, screen stack, footer bar and the single global
/// key route. Screens never handle Tab, Esc, Q or F1 themselves.
/// </summary>
internal sealed class DoopassWindow : Window
{
    private readonly DoopassContext _context;
    private readonly View _content;
    private readonly Label _header;
    private readonly Label _divider;
    private readonly Label _footer;
    private readonly Stack<ScreenBase> _stack = new();
    private ScreenBase? _current;

    public DoopassWindow(DoopassContext context)
    {
        _context = context;

        Title = "Doopass";
        BorderStyle = LineStyle.None;
        ShadowStyle = null;
        SchemeName = Monochrome.SchemeName;

        _header = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = Monochrome.ChromeSchemeName,
        };

        _divider = new Label
        {
            Text = new string('─', 500),
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = Monochrome.AccentSchemeName,
        };

        _content = new View
        {
            // Two characters of breathing room on both sides of every screen.
            X = 2,
            Y = UiKit.ContentTop,
            Width = Dim.Fill(4),
            Height = Dim.Fill(UiKit.ContentTop + UiKit.FooterRows),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = Monochrome.SchemeName,
        };

        _footer = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = Monochrome.DimSchemeName,
        };

        Add(_header, _divider, _content, _footer);

        // The application focuses the first focusable element when the runnable is
        // started (which is this window's content container). Re-assert the screen's
        // own tab order as soon as the window is initialized.
        Initialized += (_, _) => Top?.FocusInitial();

        _stack.Push(new MainMenuScreen(_context, this));
        ShowCurrent();
    }

    public DoopassContext Context => _context;

    private ScreenBase? Top => _stack.Count > 0 ? _stack.Peek() : null;

    /// <summary>
    /// Navigates forward: the current screen becomes the back target (Esc returns
    /// to it) unless it is a transitional entry screen such as sign-in.
    /// </summary>
    public void Replace(ScreenBase screen)
    {
        DetachCurrent();

        if (_stack.Count > 0 && !_stack.Peek().KeepInHistory)
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

    /// <summary>Closes the current screen and its parent, then shows <paramref name="screen"/>.</summary>
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

    /// <summary>
    /// Re-asserts the screen's own tab order. The application focuses the first
    /// focusable element when a runnable starts (the window's content container),
    /// so this runs on the first main-loop iteration.
    /// </summary>
    public void FocusCurrent()
    {
        RefreshChrome();

        if (Top is { TabStops.Count: > 0 } top)
        {
            top.TabStops[0].SetFocus();
        }
    }

    /// <summary>Re-centres the header and footer for the current screen width.</summary>
    private void RefreshChrome()
    {
        if (Top is not { } top)
        {
            return;
        }

        var width = App?.Driver?.GetOutputBuffer().Cols ?? 0;
        _header.Text = Center($"DOOPASS   ▸   {top.ScreenTitle}", width);
        _footer.Text = Center(top.FooterHint, width);
    }

    /// <summary>Shows a message screen; the callback runs when OK is pressed.</summary>
    public void Message(string text, Action onOk) => Push(new MessageScreen(_context, this, text, onOk));

    /// <summary>Shows a yes/no confirmation; the callback runs when Yes is pressed.</summary>
    public void Confirm(string text, Action onYes) => Push(new ConfirmScreen(_context, this, text, onYes));

    /// <summary>
    /// Called from <see cref="Terminal.Gui.App.IKeyboard.KeyDown"/>, i.e. before any
    /// view sees the key. Consumes only the actions in <see cref="TuiAction"/>.
    /// </summary>
    public void RouteKey(Key key, IApplication app)
    {
        // Reached before the view tree sees the key, so nothing can have handled it
        // yet. Ignoring any incoming Handled flag also keeps cached Key instances
        // (Key.CursorDown etc.) reusable.
        if (Top is null)
        {
            return;
        }

        var focused = FocusedView();
        var context = new KeyRouteContext(
            IsTextInput: focused is TextField or TextView,
            IsList: focused is ListView,
            CanGoBack: _stack.Count > 1,
            IsReadOnlyText: focused is TextView);

        // Terminal.Gui 2.4.17 routes arrow / J-K keys into ListView's "Activate" path,
        // which leaves the selection where it is. Drive list and detail scrolling
        // explicitly instead of relying on view key bindings.
        if (focused is ListView list && TryListNavigation(list, key))
        {
            key.Handled = true;
            return;
        }

        if (focused is TextView detail)
        {
            if (TryTextScrolling(detail, key))
            {
                key.Handled = true;
                return;
            }

            // The pane is at its top/bottom edge: treat the key as focus navigation
            // so the user is never trapped inside the read-only text.
            var detailCode = StripModifiers(key.KeyCode);

            if (detailCode == Key.CursorDown.KeyCode || detailCode == Key.J.KeyCode)
            {
                FocusNext(forward: true);
                key.Handled = true;
                return;
            }

            if (detailCode == Key.CursorUp.KeyCode || detailCode == Key.K.KeyCode)
            {
                FocusNext(forward: false);
                key.Handled = true;
                return;
            }
        }

        if (!context.IsEditableText && Top is StoreScreen store)
        {
            foreach (var (shortcut, action) in StoreShortcuts)
            {
                if (IsKey(key, action))
                {
                    store.RunShortcut(shortcut);
                    key.Handled = true;
                    return;
                }
            }
        }

        switch (TuiKeyMap.Resolve(key, context))
        {
            case TuiAction.FocusNext:
                FocusNext(forward: true);
                break;
            case TuiAction.FocusPrevious:
                FocusNext(forward: false);
                break;
            case TuiAction.Back:
                Back();
                break;
            case TuiAction.Quit:
                app.RequestStop();
                break;
            case TuiAction.Help:
                ToggleHelp();
                break;
            case TuiAction.None:
            default:
                return;
        }

        key.Handled = true;
    }

    private static bool TryListNavigation(ListView list, Key key)
    {
        var code = StripModifiers(key.KeyCode);

        switch (code)
        {
            case var c when c == Key.CursorDown.KeyCode || c == Key.J.KeyCode:
                list.MoveDown();
                break;
            case var c when c == Key.CursorUp.KeyCode || c == Key.K.KeyCode:
                list.MoveUp();
                break;
            case var c when c == Key.PageDown.KeyCode:
                list.MovePageDown();
                break;
            case var c when c == Key.PageUp.KeyCode:
                list.MovePageUp();
                break;
            case var c when c == Key.Home.KeyCode:
                list.MoveHome();
                break;
            case var c when c == Key.End.KeyCode:
                list.MoveEnd();
                break;
            default:
                return false;
        }

        list.SetNeedsDraw();
        return true;
    }

    private static bool TryTextScrolling(TextView text, Key key)
    {
        var code = StripModifiers(key.KeyCode);

        var command = code switch
        {
            var c when c == Key.CursorDown.KeyCode || c == Key.J.KeyCode => Command.ScrollDown,
            var c when c == Key.CursorUp.KeyCode || c == Key.K.KeyCode => Command.ScrollUp,
            var c when c == Key.PageDown.KeyCode => Command.PageDown,
            var c when c == Key.PageUp.KeyCode => Command.PageUp,
            _ => (Command?)null,
        };

        if (command is null)
        {
            return false;
        }

        // At the top/bottom edge the pane does not move: let the key fall through to
        // focus navigation instead of trapping the user inside the text view.
        var before = text.CurrentRow;
        text.InvokeCommand(command.Value);
        text.SetNeedsDraw();

        return text.CurrentRow != before;
    }

    private static Terminal.Gui.Drivers.KeyCode StripModifiers(Terminal.Gui.Drivers.KeyCode code) =>
        code & ~(Terminal.Gui.Drivers.KeyCode.ShiftMask
                 | Terminal.Gui.Drivers.KeyCode.AltMask
                 | Terminal.Gui.Drivers.KeyCode.CtrlMask);

    private static readonly (char Shortcut, Key Key)[] StoreShortcuts =
    [
        ('v', Key.V),
        ('e', Key.E),
        ('d', Key.D),
        ('a', Key.A),
    ];

    private static bool IsKey(Key key, Key expected) => StripModifiers(key.KeyCode) == expected.KeyCode;

    /// <summary>
    /// The deepest view that has focus. <see cref="IApplication.Navigation"/> can
    /// report the focused container instead of the focused control, which would make
    /// "is the user typing / is a list focused" checks wrong.
    /// </summary>
    /// <summary>
    /// The deepest view that actually has focus. Application-level focus bookkeeping
    /// can lag (or point at the content container), so walk the live view tree.
    /// </summary>
    internal View? FocusedViewForTests() => FocusedView();

    private View? FocusedView()
    {
        View? found = null;

        void Walk(View view)
        {
            if (!view.HasFocus)
            {
                return;
            }

            found = view;

            foreach (var child in view.SubViews)
            {
                Walk(child);
            }
        }

        Walk(this);
        return found;
    }

    private void FocusNext(bool forward)
    {
        var top = Top!;
        var stops = top.TabStops;

        if (stops.Count == 0)
        {
            return;
        }

        var focused = FocusedView();
        var current = -1;

        for (var i = 0; i < stops.Count; i++)
        {
            if (ReferenceEquals(stops[i], focused))
            {
                current = i;
                break;
            }
        }

        var next = FocusRing.Next(current, stops.Count, forward);

        if (next >= 0)
        {
            stops[next].SetFocus();
        }
    }

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

        RefreshChrome();

        App?.LayoutAndDraw(true);

        if (top.TabStops.Count > 0)
        {
            top.TabStops[0].SetFocus();
        }

        App?.LayoutAndDraw();
    }

    /// <summary>Pads text so it is centred on a fixed-width row.</summary>
    private static string Center(string text, int width)
    {
        var padding = Math.Max(0, (width - text.Length) / 2);
        return new string(' ', padding) + text;
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
