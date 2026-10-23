using Doopass.Tui.Ui;
using Xunit;

namespace Doopass.Tui.Tests;

public class FocusRingTests
{
    [Fact]
    public void Next_wraps_around() => Assert.Equal(0, FocusRing.Next(2, 3, forward: true));

    [Fact]
    public void Previous_wraps_backwards() => Assert.Equal(2, FocusRing.Next(0, 3, forward: false));

    [Fact]
    public void Unknown_current_focus_starts_at_the_first_stop() =>
        Assert.Equal(0, FocusRing.Next(-1, 3, forward: true));

    [Fact]
    public void Empty_ring_returns_minus_one() => Assert.Equal(-1, FocusRing.Next(0, 0, forward: true));
}
