using System.Net;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The host serves the embedded Blazor WebAssembly bundle: client routes fall
/// back to index.html, framework assets exist, and unknown API routes stay 404s
/// instead of being masked by the SPA shell.
/// </summary>
public sealed class WebHostStaticTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task Client_route_serves_the_spa_shell()
    {
        using var response = await fixture.Client.GetAsync("/store");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"app\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Framework_asset_is_served()
    {
        using var response = await fixture.Client.GetAsync("/_framework/blazor.webassembly.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Framework_assets_are_compressed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_framework/blazor.webassembly.js");
        request.Headers.AcceptEncoding.ParseAdd("br, gzip");

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Content.Headers.ContentEncoding,
            encoding => encoding is "br" or "gzip");
    }

    [Fact]
    public async Task Unknown_api_route_is_not_the_spa_fallback()
    {
        using var response = await fixture.Client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
