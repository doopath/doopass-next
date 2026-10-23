namespace Doopass.Tui.Ui;

/// <summary>Index arithmetic for an explicit, per-screen tab order.</summary>
internal static class FocusRing
{
    public static int Next(int current, int count, bool forward)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count)
        {
            return forward ? 0 : count - 1;
        }

        return forward ? (current + 1) % count : (current - 1 + count) % count;
    }
}
