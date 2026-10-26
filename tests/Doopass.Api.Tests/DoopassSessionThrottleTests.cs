using Doopass.Cli.Services;
using Doopass.Core;
using Doopass.Core.Configuration;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The web page logs in through <see cref="DoopassSession"/>, not through the
/// REST endpoint, so the throttle has to live there as well.
/// </summary>
public sealed class DoopassSessionThrottleTests : IDisposable
{
    private readonly string _root;

    public DoopassSessionThrottleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "doopass-session-throttle", Guid.NewGuid().ToString("N"));
        AppPaths.OverrideRoot(_root);
    }

    public void Dispose()
    {
        AppPaths.ResetOverride();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task LoginAsync_is_refused_after_the_threshold()
    {
        var (session, _) = CreateSession();
        Assert.True((await session.InitAsync("Session test 1234")).Success);

        for (var attempt = 0; attempt < AuthThrottle.MaxFailuresPerWindow; attempt++)
        {
            var (success, message) = await session.LoginAsync("wrong password");
            Assert.False(success);
            Assert.Equal("Incorrect password!", message);
        }

        var (blocked, blockedMessage) = await session.LoginAsync("Session test 1234");

        Assert.False(blocked);
        Assert.Contains("Too many attempts", blockedMessage);
    }

    [Fact]
    public async Task A_successful_login_clears_the_failures()
    {
        var (session, throttle) = CreateSession();
        Assert.True((await session.InitAsync("Session test 1234")).Success);

        Assert.False((await session.LoginAsync("wrong password")).Success);
        Assert.True((await session.LoginAsync("Session test 1234")).Success);

        Assert.Null(throttle.GetRetryAfter());

        session.Logout();

        for (var attempt = 0; attempt < AuthThrottle.MaxFailuresPerWindow - 1; attempt++)
        {
            Assert.False((await session.LoginAsync("wrong password")).Success);
        }

        Assert.Null(throttle.GetRetryAfter());
    }

    private (DoopassSession Session, AuthThrottle Throttle) CreateSession()
    {
        var throttle = new AuthThrottle();
        return (new DoopassSession(new Settings(), throttle), throttle);
    }
}
