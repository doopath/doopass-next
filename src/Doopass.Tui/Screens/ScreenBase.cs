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

    /// <summary>Gives focus to the first tab stop on this screen.</summary>
    public void FocusInitial()
    {
        if (_tabStops.Count > 0)
        {
            _tabStops[0].SetFocus();
        }
    }
}
