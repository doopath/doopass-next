using Doopass.Cli.Api;
using Doopass.Cli.Services;
using Doopass.Core;
using Doopass.Core.Configuration;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The web page logs in through <see cref="DoopassSession"/>, not through the
/// REST endpoint. Failures may slow that path down, but they must never make the
/// owner's correct password fail: a LAN attacker cannot take the vault away.
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
    public async Task Correct_password_still_logs_in_after_failures()
    {
        var (session, throttle, delays) = CreateSession();
        Assert.True((await session.InitAsync("Session test 1234")).Success);

        // Five wrong attempts: exactly what used to trigger the hard lockout.
        for (var attempt = 0; attempt < AuthThrottle.DelayStartsAfter; attempt++)
        {
            var (wrong, wrongMessage) = await session.LoginAsync("wrong password");
            Assert.False(wrong);
            Assert.Equal("Incorrect password!", wrongMessage);
        }

        var (success, message) = await session.LoginAsync("Session test 1234");

        Assert.True(success);
        Assert.Equal(string.Empty, message);

        // The owner was slowed down, never refused, and a success clears the count.
        Assert.Contains(delays, delay => delay > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, throttle.DelayBeforeAttempt());
    }

    [Fact]
    public async Task A_successful_login_clears_the_failures()
    {
        var (session, throttle, _) = CreateSession();
        Assert.True((await session.InitAsync("Session test 1234")).Success);

        Assert.False((await session.LoginAsync("wrong password")).Success);
        Assert.True((await session.LoginAsync("Session test 1234")).Success);

        Assert.Equal(TimeSpan.Zero, throttle.DelayBeforeAttempt());

        session.Logout();

        for (var attempt = 0; attempt < AuthThrottle.DelayStartsAfter - 1; attempt++)
        {
            Assert.False((await session.LoginAsync("wrong password")).Success);
        }

        Assert.Equal(TimeSpan.Zero, throttle.DelayBeforeAttempt());
    }

    [Fact]
    public async Task Foreign_guessing_does_not_block_the_owner()
    {
        var (session, throttle, _) = CreateSession();
        Assert.True((await session.InitAsync("Owner test 1234")).Success);

        // Someone on the LAN hammers the REST login. The handler applies the
        // computed delay itself; the coordinator call underneath must not refuse.
        var coordinator = new ApiVaultCoordinator(new Settings(), new VaultChangeNotifier(), throttle);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var (success, _, error) = coordinator.Login("not the password");
            Assert.False(success);
            Assert.Equal("Incorrect password!", error);
        }

        // The owner's browser session still gets in, after the pause.
        var (ownerIn, message) = await session.LoginAsync("Owner test 1234");

        Assert.True(ownerIn);
        Assert.Equal(string.Empty, message);
    }

    /// <summary>A session whose throttle records delays instead of sleeping on them.</summary>
    private static (DoopassSession Session, AuthThrottle Throttle, List<TimeSpan> Delays) CreateSession()
    {
        var delays = new List<TimeSpan>();
        var throttle = new AuthThrottle
        {
            DelayHook = delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
        };

        return (new DoopassSession(new Settings(), throttle), throttle, delays);
    }
}
