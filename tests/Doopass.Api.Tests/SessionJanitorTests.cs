using System.Net.Http.Json;
using Doopass.Cli.Api;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The vault must relock on its own when the last client goes away — without
/// waiting for another API request to trigger the lazy prune.
/// </summary>
public sealed class SessionJanitorTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task Janitor_relocks_past_the_ttl_without_any_request()
    {
        fixture.Reset();
        await fixture.InitAsync("Janitor test 1234");
        var atInit = fixture.Coordinator.Clock();

        // Nothing calls the API after this point; only the janitor may act.
        fixture.Coordinator.Clock = () => atInit.Add(fixture.Coordinator.SessionTtl).AddSeconds(1);

        using var janitor = new SessionJanitor(fixture.Coordinator)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };

        await janitor.StartAsync(CancellationToken.None);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);

            while (fixture.Coordinator.SessionCount > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
        finally
        {
            await janitor.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, fixture.Coordinator.SessionCount);

        var state = await fixture.Client.GetFromJsonAsync<State>("/api/vault/state");
        Assert.False(state!.IsUnlocked);
    }

    private sealed record State(bool StoreExists, bool IsUnlocked, string ServerVersion);
}
