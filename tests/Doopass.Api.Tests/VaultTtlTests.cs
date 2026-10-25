using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>Sliding TTL expiry and relock (numbered; fake clock on the coordinator; one auth attempt per test).</summary>
[TestCaseOrderer("Doopass.Api.Tests.AlphabeticalTestCaseOrderer", "Doopass.Api.Tests")]
public sealed class VaultTtlTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Token_survives_a_fresh_ttl_window()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("TTL test 1234");
        var atInit = fixture.Coordinator.Clock();

        // Move the fake clock forward just under the TTL: the token stays valid.
        fixture.Coordinator.Clock = () => atInit.Add(fixture.Coordinator.SessionTtl).AddSeconds(-30);
        var keys = await fixture.Client.AuthSend(HttpMethod.Get, "/api/vault/keys", token);
        Assert.Equal(HttpStatusCode.OK, keys.StatusCode);
    }

    [Fact]
    public async Task T02_Token_expires_past_the_ttl_and_relocks()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("TTL test 1234");
        var atInit = fixture.Coordinator.Clock();

        fixture.Coordinator.Clock = () => atInit.Add(fixture.Coordinator.SessionTtl).AddSeconds(30);
        fixture.Coordinator.PruneExpiredSessions();

        var keys = await fixture.Client.AuthSend(HttpMethod.Get, "/api/vault/keys", token);
        Assert.Equal(HttpStatusCode.Unauthorized, keys.StatusCode);

        // The coordinator dropped the store: the state reports unlocked=false.
        var state = await fixture.Client.GetFromJsonAsync<State>("/api/vault/state");
        Assert.False(state!.IsUnlocked);
    }

    [Fact]
    public async Task T03_Logout_of_the_last_session_relocks_the_coordinator()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Relock test 1234");

        Assert.True(fixture.Coordinator.SessionCount > 0);
        fixture.Coordinator.Logout(token);
        Assert.Equal(0, fixture.Coordinator.SessionCount);

        var state = await fixture.Client.GetFromJsonAsync<State>("/api/vault/state");
        Assert.False(state!.IsUnlocked);
    }

    private sealed record State(bool StoreExists, bool IsUnlocked, string ServerVersion);
}
