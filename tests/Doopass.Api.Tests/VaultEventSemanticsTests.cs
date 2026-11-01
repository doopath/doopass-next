using Doopass.Cli.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// "The vault locked" and "this token expired" are different facts. Treating
/// every TTL expiry as a vault lock navigated browser circuits back to the login
/// page although their own session was still unlocked.
/// </summary>
public sealed class VaultEventSemanticsTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Expiring_one_token_is_not_a_vault_lock()
    {
        fixture.Reset();

        var t0 = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        fixture.Coordinator.Clock = () => t0;
        var (first, _) = await fixture.InitAsync("Events test 1234");

        fixture.Coordinator.Clock = () => t0.AddMinutes(10);
        var second = await fixture.LoginAsync("Events test 1234");
        Assert.NotEqual(first, second);

        var notifier = fixture.App.Services.GetRequiredService<IVaultChangeNotifier>();
        var events = new List<string>();

        void OnLocked(string _) => events.Add("locked");
        void OnExpired(string token) => events.Add($"expired:{token}");

        notifier.VaultLocked += OnLocked;
        notifier.SessionExpired += OnExpired;

        try
        {
            // The first token (t0) is past its TTL; the second one (t0+10) is not.
            fixture.Coordinator.Clock = () => t0.AddMinutes(35);
            fixture.Coordinator.PruneExpiredSessions();

            Assert.Equal([$"expired:{first}"], events);
            Assert.Equal(1, fixture.Coordinator.SessionCount);
            Assert.True(fixture.Coordinator.GetState().IsUnlocked);

            // Now the last session expires: that really is a vault lock.
            events.Clear();
            fixture.Coordinator.Clock = () => t0.AddMinutes(45);
            fixture.Coordinator.PruneExpiredSessions();

            Assert.Equal([$"expired:{second}", "locked"], events);
            Assert.Equal(0, fixture.Coordinator.SessionCount);
            Assert.False(fixture.Coordinator.GetState().IsUnlocked);
        }
        finally
        {
            notifier.VaultLocked -= OnLocked;
            notifier.SessionExpired -= OnExpired;
        }
    }
}
