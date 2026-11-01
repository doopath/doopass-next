using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The sliding TTL alone lets an active token live forever. An absolute cap
/// (8 h by default) must expire it even when every request refreshes it.
/// </summary>
public sealed class AbsoluteSessionTtlTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Active_token_expires_at_the_absolute_limit()
    {
        fixture.Reset();

        var t0 = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);
        fixture.Coordinator.Clock = () => t0;
        var (token, _) = await fixture.InitAsync("Absolute TTL 1234");

        Assert.Equal(TimeSpan.FromHours(8), fixture.Coordinator.AbsoluteSessionTtl);

        // Stay active: a request every 20 minutes keeps the sliding TTL fresh all
        // the way to just before the absolute cap.
        for (var minutes = 20; minutes <= 475; minutes += 20)
        {
            var at = minutes;
            fixture.Coordinator.Clock = () => t0.AddMinutes(at);
            Assert.Equal(token, fixture.Coordinator.TouchToken(token));
        }

        var active = await fixture.Client.AuthSend(HttpMethod.Get, "/api/vault/keys", token);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);

        // Past the cap while still inside the sliding window: rejected.
        fixture.Coordinator.Clock = () => t0.AddHours(8).AddMinutes(1);
        var expired = await fixture.Client.AuthSend(HttpMethod.Get, "/api/vault/keys", token);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        var state = await fixture.Client.GetFromJsonAsync<State>("/api/vault/state");
        Assert.False(state!.IsUnlocked);
    }

    private sealed record State(bool StoreExists, bool IsUnlocked, string ServerVersion);
}
