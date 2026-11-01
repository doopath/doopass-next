using Doopass.Cli.Services;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The Blazor login path has no ASP.NET rate limiter in front of it. This
/// throttle must slow guessing down without ever locking the owner out: it only
/// answers how long to wait before the password check.
/// </summary>
public sealed class AuthThrottleTests
{
    private readonly AuthThrottle _throttle;
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public AuthThrottleTests()
    {
        _throttle = new AuthThrottle { Clock = () => _now };
    }

    [Fact]
    public void Four_failures_do_not_delay_the_next_attempt()
    {
        Fail(AuthThrottle.DelayStartsAfter - 1);

        Assert.Equal(TimeSpan.Zero, _throttle.DelayBeforeAttempt());
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 4)]
    [InlineData(8, 5)]
    [InlineData(20, 5)]
    public void Delay_grows_after_five_failures_and_is_capped(int failures, int seconds)
    {
        Fail(failures);

        Assert.Equal(TimeSpan.FromSeconds(seconds), _throttle.DelayBeforeAttempt());
    }

    [Fact]
    public void Window_expiry_removes_the_delay_without_any_lockout()
    {
        Fail(AuthThrottle.DelayStartsAfter);
        Assert.NotEqual(TimeSpan.Zero, _throttle.DelayBeforeAttempt());

        _now += TimeSpan.FromSeconds(61);

        Assert.Equal(TimeSpan.Zero, _throttle.DelayBeforeAttempt());
    }

    [Fact]
    public void The_delay_never_refuses_an_attempt()
    {
        // Ten failures used to add a 30 s lockout. The delay only says "wait";
        // there is no state in which an attempt is forbidden.
        Fail(10);

        var delay = _throttle.DelayBeforeAttempt();

        Assert.True(delay > TimeSpan.Zero);
        Assert.True(delay <= AuthThrottle.MaxDelay);
    }

    [Fact]
    public void Success_resets_the_counter_and_the_delay()
    {
        Fail(10);
        Assert.Equal(AuthThrottle.MaxDelay, _throttle.DelayBeforeAttempt());

        _throttle.RegisterSuccess();

        Assert.Equal(TimeSpan.Zero, _throttle.DelayBeforeAttempt());
    }

    [Fact]
    public void Reset_clears_everything()
    {
        Fail(10);
        Assert.NotEqual(TimeSpan.Zero, _throttle.DelayBeforeAttempt());

        _throttle.Reset();

        Assert.Equal(TimeSpan.Zero, _throttle.DelayBeforeAttempt());
    }

    private void Fail(int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            _throttle.RegisterFailure();
        }
    }
}
