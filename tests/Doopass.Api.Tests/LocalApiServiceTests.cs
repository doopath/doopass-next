using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Doopass.Cli.Services;

namespace Doopass.Api.Tests;

/// <summary>
/// The web host's <see cref="IVaultSession"/> client. The GET reads (keys, value,
/// backups, settings) previously sent no <c>Authorization</c> header, so every
/// read endpoint returned 401 and the Store page showed "Session expired or
/// unauthorized." These tests pin that the token is attached on GET too.
/// </summary>
public sealed class LocalApiServiceTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task Read_endpoints_send_bearer_token_after_login()
    {
        fixture.Reset();
        const string password = "LocalApiService bearer test 1234";
        await fixture.InitAsync(password);

        // Seed a pair through the authenticated API client so there is data to read.
        await fixture.Client.AuthSend(
            HttpMethod.Post, "/api/vault/pairs", await fixture.LoginAsync(password),
            VaultApiTestsHelpers.Json(new { key = "github", value = "s3cret" }));

        var factory = fixture.App.Services.GetRequiredService<IHttpClientFactory>();
        var session = new LocalApiService(factory, fixture.Port, fixture.Coordinator);

        var (ok, _) = await session.LoginAsync(password);
        Assert.True(ok, "login must succeed");

        // GET /api/vault/keys must carry the token; without it this throws 401.
        var keys = await session.GetKeysAsync();
        Assert.Contains("github", keys);

        // GET /api/vault/value is a second GET that must carry the token as well.
        var value = await session.GetValueAsync("github");
        Assert.Equal("s3cret", value);
    }
}
