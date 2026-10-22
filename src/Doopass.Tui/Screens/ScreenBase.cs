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
    }

    protected DoopassContext Context { get; }

    protected DoopassWindow Shell { get; }

    /// <summary>Gives focus to the first focusable subview.</summary>
    public void FocusInitial() => FindFocusable(this)?.SetFocus();

    private static View? FindFocusable(View view)
    {
        if (view.CanFocus)
        {
            return view;
        }

        foreach (var subView in view.SubViews)
        {
            var found = FindFocusable(subView);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
