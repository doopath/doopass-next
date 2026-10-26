using System.Net;
using Doopass.Cli.Api;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The web UI is a local, single-origin app: it must never be embedded by
/// another site, sniffed into another content type, or allowed to load
/// third-party resources.
/// </summary>
public sealed class SecurityHeadersTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/api/vault/state")]
    public async Task Security_headers_are_present(string path)
    {
        var response = await fixture.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var headers = response.Headers;
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", headers.GetValues("Referrer-Policy").Single());

        var csp = headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
    }

    [Fact]
    public async Task Hsts_is_only_sent_over_tls()
    {
        var response = await fixture.Client.GetAsync("/api/vault/state");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.5")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void Hsts_is_not_pinned_on_loopback_hosts(string? host)
    {
        // A remembered HSTS pin would force the next plain http://localhost run
        // onto TLS and break it with a self-signed certificate error.
        Assert.False(WebHostFactory.ShouldSendHsts(host));
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.2")]
    [InlineData("doopass.lan")]
    [InlineData("example.com")]
    public void Hsts_is_pinned_on_network_hosts(string host)
    {
        Assert.True(WebHostFactory.ShouldSendHsts(host));
    }
}
