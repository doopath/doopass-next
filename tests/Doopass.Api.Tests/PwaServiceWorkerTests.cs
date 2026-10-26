using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The service worker must not touch /api, /hubs or /_blazor: caching a
/// /api/vault/value response would persist a decrypted secret in Cache Storage.
/// </summary>
public sealed class PwaServiceWorkerTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task Service_worker_bypasses_api_hub_and_circuit_traffic()
    {
        var script = await fixture.Client.GetStringAsync("/pwa/sw.js");

        Assert.Contains("doopass-pwa-v3", script);
        Assert.Contains("startsWith('/_blazor')", script);
        Assert.Contains("startsWith('/hubs/')", script);
        Assert.Contains("startsWith('/api/')", script);
    }
}
