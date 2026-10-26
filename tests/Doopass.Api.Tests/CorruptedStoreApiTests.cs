using System.Net;
using System.Net.Http.Json;
using Doopass.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>A damaged store file is a client-visible state (422), not a 500 and not "wrong password".</summary>
public sealed class CorruptedStoreApiTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Login_reports_a_corrupted_store()
    {
        fixture.Reset();

        var settings = fixture.App.Services.GetRequiredService<Settings>();
        Directory.CreateDirectory(Path.GetDirectoryName(settings.StoreFile)!);
        await File.WriteAllTextAsync(settings.StoreFile, "AAAA");

        var response = await fixture.Client.PostAsJsonAsync("/api/auth/login", new { password = "Corrupt test 1234" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("corrupted", body);
        Assert.DoesNotContain("System.", body);
        Assert.DoesNotContain("Exception", body);
    }
}
