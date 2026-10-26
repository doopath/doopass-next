using Doopass.Cli.Services;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The Blazor login path has no ASP.NET rate limiter in front of it, so this
/// class is the only brake on master-password guessing there.
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
    public void Five_failures_block_the_sixth_attempt()
    {
        for (var attempt = 0; attempt < AuthThrottle.MaxFailuresPerWindow; attempt++)
        {
            Assert.Null(_throttle.GetRetryAfter());
            _throttle.RegisterFailure();
        }

        var retryAfter = _throttle.GetRetryAfter();

        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(59), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Retry_after_shrinks_as_the_window_slides()
    {
        Fail(AuthThrottle.MaxFailuresPerWindow);

        _now += TimeSpan.FromSeconds(20);
        var retryAfter = _throttle.GetRetryAfter();

        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(39), TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Window_expiry_lets_the_next_attempt_through()
    {
        Fail(AuthThrottle.MaxFailuresPerWindow);
        _now += TimeSpan.FromSeconds(61);

        Assert.Null(_throttle.GetRetryAfter());
    }

    [Fact]
    public void Success_resets_the_counter()
    {
        Fail(AuthThrottle.MaxFailuresPerWindow);
        Assert.NotNull(_throttle.GetRetryAfter());

        _throttle.RegisterSuccess();

        Assert.Null(_throttle.GetRetryAfter());
    }

    [Fact]
    public void Ten_consecutive_failures_lock_for_thirty_seconds()
    {
        Fail(AuthThrottle.MaxFailuresPerWindow);
        _now += TimeSpan.FromSeconds(61);
        Fail(AuthThrottle.MaxFailuresPerWindow);

        var retryAfter = _throttle.GetRetryAfter();
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));

        _now += TimeSpan.FromSeconds(31);
        Assert.Null(_throttle.GetRetryAfter());
    }

    [Fact]
    public void Reset_clears_everything()
    {
        Fail(AuthThrottle.FailuresBeforeLockout);

        _throttle.Reset();

        Assert.Null(_throttle.GetRetryAfter());
    }

    private void Fail(int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            _throttle.RegisterFailure();
        }
    }
}
