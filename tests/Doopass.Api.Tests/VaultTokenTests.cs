using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>Token lifecycle: issue, use, logout, invalidation (one auth attempt total).</summary>
public sealed class VaultTokenTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Logout_invalidates_every_protected_endpoint()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Logout test 1234");
        Assert.NotEqual(string.Empty, token);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Authorization = new("Bearer", token);
        var logoutResponse = await fixture.Client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var keys = new HttpRequestMessage(HttpMethod.Get, "/api/vault/keys");
        keys.Headers.Authorization = new("Bearer", token);
        var keysResponse = await fixture.Client.SendAsync(keys);
        Assert.Equal(HttpStatusCode.Unauthorized, keysResponse.StatusCode);

        var body = await keysResponse.Content.ReadAsStringAsync();
        Assert.Equal("""{"error":"Session expired or unauthorized."}""", body);

        using var pairs = new HttpRequestMessage(HttpMethod.Post, "/api/vault/pairs")
        {
            Content = JsonContent.Create(new { key = "x", value = "y" }),
        };
        pairs.Headers.Authorization = new("Bearer", token);
        var add = await fixture.Client.SendAsync(pairs);
        Assert.Equal(HttpStatusCode.Unauthorized, add.StatusCode);
    }
}
