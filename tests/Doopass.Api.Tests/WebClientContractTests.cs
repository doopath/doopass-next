using Doopass.WebClient;
using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// Drives the real browser session against the real host. The unit tests in
/// Doopass.WebClient.Tests feed hand-written JSON, so only this test catches a
/// server DTO rename that the WASM client would fail to parse.
/// </summary>
public sealed class WebClientContractTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task WebApiVaultSession_drives_the_real_host_end_to_end()
    {
        fixture.Reset();

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{fixture.Port}") };
        using var session = new WebApiVaultSession(http, new NoStorage(), new VaultEventRelay());

        var (ok, message) = await session.InitAsync("integration pass 1234");
        Assert.True(ok, message);

        await session.AddAsync("github", "s3cret");

        Assert.Contains("github", await session.GetKeysAsync());
        Assert.Equal("s3cret", await session.GetValueAsync("github"));

        var state = await session.GetStateAsync();
        Assert.True(state.StoreExists);
        Assert.True(state.IsUnlocked);

        await session.RemoveAsync("github");
        Assert.Empty(await session.GetKeysAsync());

        await session.LogoutAsync();
        Assert.False((await session.GetStateAsync()).IsUnlocked);
    }

    /// <summary>sessionStorage is irrelevant to the contract; the token stays in memory.</summary>
    private sealed class NoStorage : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
