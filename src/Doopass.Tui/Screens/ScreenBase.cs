using Doopass.Tui.Theme;
using Terminal.Gui.ViewBase;

namespace Doopass.Tui.Screens;

/// <summary>Base class for all TUI screens.</summary>
internal abstract class ScreenBase : View
{
    private readonly List<View> _tabStops = new();

    protected ScreenBase(DoopassContext context, DoopassWindow shell)
    {
        Context = context;
        Shell = shell;

        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // Screens are containers, never focus targets themselves: the application
        // must focus the first real control (list, field, button) instead of the
        // container. Children remain focusable.
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        SchemeName = Monochrome.SchemeName;
    }

    protected DoopassContext Context { get; }

    protected DoopassWindow Shell { get; }

    /// <summary>Text for the header bar.</summary>
    public abstract string ScreenTitle { get; }

    /// <summary>Contextual hotkeys for the footer bar.</summary>
    public abstract string FooterHint { get; }

    /// <summary>
    /// Whether this screen stays available as a back target when it is replaced.
    /// Entry screens (sign in, init store) are transitional: going back to them is
    /// meaningless, so they are dropped and the screen below them becomes "back".
    /// </summary>
    public virtual bool KeepInHistory => true;

    /// <summary>Controls in the order Tab/Shift+Tab should visit them.</summary>
    public IReadOnlyList<View> TabStops => _tabStops;

    /// <summary>Registers a focusable control in the screen's tab order.</summary>
    protected T AddStop<T>(T view) where T : View
    {
        _tabStops.Add(view);
        return view;
    }

    /// <summary>
    /// Centres a fixed-size view after each layout pass. Computing the offset here
    /// avoids <c>Pos.Center() - width/2</c>, which evaluates against a zero-width
    /// superview during construction and leaves the view clipped.
    /// </summary>
    protected void CenterView(View view, int width)
    {
        SubViewLayout += (_, _) => view.X = Math.Max(0, (Viewport.Width - width) / 2);
    }

    /// <summary>Centres a fixed-size view on both axes (dialogs and forms).</summary>
    protected void CenterView(View view, int width, int height)
    {
        SubViewLayout += (_, _) =>
        {
            view.X = Math.Max(0, (Viewport.Width - width) / 2);
            view.Y = Math.Max(0, (Viewport.Height - height) / 2);
        };
    }

    /// <summary>Called when the shell moves focus to a control on this screen.</summary>
    public virtual void OnFocusMoved(View? focused)
    {
    }

    /// <summary>Called every time this screen becomes the visible one.</summary>
    public virtual void OnShown()
    {
    }

    /// <summary>Gives focus to the first tab stop on this screen.</summary>
    public void FocusInitial()
    {
        if (_tabStops.Count > 0)
        {
            _tabStops[0].SetFocus();
        }
    }
}
