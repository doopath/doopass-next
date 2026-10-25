using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>No-body, no-auth retries against /api/auth endpoints trip the per-IP limiter (own host).</summary>
public sealed class RateLimitTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Six_bad_logins_trip_the_limiter_429()
    {
        fixture.Reset();
        await fixture.InitAsync("Limit test 1234");

        var statusCodes = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await fixture.Client.PostAsJsonAsync("/api/auth/login", new { password = "nope 1234" });
            statusCodes.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statusCodes[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statusCodes[5]);
    }

    [Fact]
    public async Task T02_Rejection_body_is_the_json_error_shape()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/auth/init", new { password = "x" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body);
    }
}
