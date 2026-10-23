using Doopass.Tui.Theme;
using Terminal.Gui.ViewBase;

namespace Doopass.Tui.Screens;

/// <summary>Base class for all TUI screens.</summary>
internal abstract class ScreenBase : View
{
    protected ScreenBase(DoopassContext context, DoopassWindow shell)
    {
        Context = context;
        Shell = shell;

        Width = Dim.Fill();
        Height = Dim.Fill();

        // A SuperView must be focusable for its children to receive focus, but it
        // must not become a tab stop itself (that would steal focus from the controls).
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        SchemeName = Monochrome.SchemeName;
    }

    protected DoopassContext Context { get; }

    protected DoopassWindow Shell { get; }

    /// <summary>Gives focus to the first focusable control on this screen.</summary>
    public void FocusInitial()
    {
        var view = FindFocusableStop(this);

        if (view is not null)
        {
            view.SetFocus();
        }
    }

    private static View? FindFocusableStop(View parent)
    {
        foreach (var child in parent.SubViews)
        {
            if (child.CanFocus && child.TabStop != TabBehavior.NoStop)
            {
                return child;
            }

            var found = FindFocusableStop(child);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
