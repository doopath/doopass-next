using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>Settings REST surface: read, modify, restore defaults (substituted values; zero login attempts).</summary>
public sealed class VaultSettingsTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Settings_round_trip_and_restore_defaults()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Settings test 1234");
        var client = fixture.Client;

        // The UI shows substituted (absolute) paths, so APPDATA/ values resolve.
        var sections = (await (await client.AuthSend(HttpMethod.Get, "/api/settings", token)).Content
            .ReadFromJsonAsync<SettingsPayload>())!.Sections;
        Assert.NotEmpty(sections);

        var originalValue = sections.Single(section => section.Name == "local").Pairs["store_file"];

        // Change the store path: the returned value reflects the change.
        var put = await client.AuthSend(HttpMethod.Put, "/api/settings/local/store_file", token,
            VaultApiTestsHelpers.Json(new { value = "APPDATA/custom-store.enc" }));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var afterPut = (await (await client.AuthSend(HttpMethod.Get, "/api/settings", token)).Content
            .ReadFromJsonAsync<SettingsPayload>())!.Sections;
        var changed = afterPut.Single(section => section.Name == "local").Pairs["store_file"];
        Assert.NotEqual(originalValue, changed);
        Assert.EndsWith("custom-store.enc", changed, StringComparison.OrdinalIgnoreCase);

        // Unknown section -> 404.
        var unknownSection = await client.AuthSend(HttpMethod.Put, "/api/settings/nope/store_file", token,
            VaultApiTestsHelpers.Json(new { value = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, unknownSection.StatusCode);

        // Restore defaults resets the section values to the default config.
        var restore = await client.AuthSend(HttpMethod.Post, "/api/settings/restore-defaults", token);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        var afterRestore = (await (await client.AuthSend(HttpMethod.Get, "/api/settings", token)).Content
            .ReadFromJsonAsync<SettingsPayload>())!.Sections;
        var restored = afterRestore.Single(section => section.Name == "local").Pairs["store_file"];
        Assert.Equal(originalValue, restored);
    }

    private sealed record SettingsPayload(IReadOnlyList<SectionDto> Sections);

    private sealed record SectionDto(string Name, IReadOnlyDictionary<string, string> Pairs);
}
