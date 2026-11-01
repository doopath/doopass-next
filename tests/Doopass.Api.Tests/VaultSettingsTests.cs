using System.Net;
using System.Net.Http.Json;
using Doopass.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        var local = sections.Single(section => section.Name == "local");
        var originalStoreFile = local.Pairs["store_file"];
        var originalExtension = local.Pairs["backup_extension"];

        // Path settings are read-only: a writable store_file would let the settings
        // endpoint overwrite any file with ciphertext on the next save.
        var storeFilePut = await client.AuthSend(HttpMethod.Put, "/api/settings/local/store_file", token,
            VaultApiTestsHelpers.Json(new { value = "APPDATA/custom-store.enc" }));
        Assert.Equal(HttpStatusCode.Forbidden, storeFilePut.StatusCode);
        Assert.Contains("read-only", await storeFilePut.Content.ReadAsStringAsync());

        // Unknown keys are read-only too (never persisted).
        var unknownKey = await client.AuthSend(HttpMethod.Put, "/api/settings/local/nope_key", token,
            VaultApiTestsHelpers.Json(new { value = "x" }));
        Assert.Equal(HttpStatusCode.Forbidden, unknownKey.StatusCode);

        // Unknown section -> 404 (resolved by Settings itself).
        var unknownSection = await client.AuthSend(HttpMethod.Put, "/api/settings/nope/store_file", token,
            VaultApiTestsHelpers.Json(new { value = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, unknownSection.StatusCode);

        // An empty backup extension is rejected: it would turn backup deletion into
        // "delete any file with this name".
        var badExtension = await client.AuthSend(HttpMethod.Put, "/api/settings/local/backup_extension", token,
            VaultApiTestsHelpers.Json(new { value = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, badExtension.StatusCode);

        // A valid extension is accepted and reported back.
        var goodExtension = await client.AuthSend(HttpMethod.Put, "/api/settings/local/backup_extension", token,
            VaultApiTestsHelpers.Json(new { value = ".enc.bak2" }));
        Assert.Equal(HttpStatusCode.NoContent, goodExtension.StatusCode);

        var afterPut = (await (await client.AuthSend(HttpMethod.Get, "/api/settings", token)).Content
            .ReadFromJsonAsync<SettingsPayload>())!.Sections;
        var changed = afterPut.Single(section => section.Name == "local").Pairs;

        Assert.Equal(originalStoreFile, changed["store_file"]);
        Assert.Equal(".enc.bak2", changed["backup_extension"]);

        // Restore defaults resets the section values to the default config.
        var restore = await client.AuthSend(HttpMethod.Post, "/api/settings/restore-defaults", token);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        var afterRestore = (await (await client.AuthSend(HttpMethod.Get, "/api/settings", token)).Content
            .ReadFromJsonAsync<SettingsPayload>())!.Sections;
        var restored = afterRestore.Single(section => section.Name == "local").Pairs;

        Assert.Equal(originalStoreFile, restored["store_file"]);
        Assert.Equal(originalExtension, restored["backup_extension"]);
    }

    [Fact]
    public async Task T02_TlsPassword_is_not_returned()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("TLS settings 1234");
        var settings = fixture.App.Services.GetRequiredService<Settings>();

        try
        {
            settings.SetTls("/tmp/doopass.pfx", "super-secret");

            var sections = (await (await fixture.Client.AuthSend(HttpMethod.Get, "/api/settings", token)).Content
                .ReadFromJsonAsync<SettingsPayload>())!.Sections;
            var api = sections.Single(section => section.Name == "api");

            Assert.Equal("/tmp/doopass.pfx", api.Pairs["tls_pfx"]);
            Assert.False(api.Pairs.ContainsKey("tls_pass"));
        }
        finally
        {
            settings.RestoreDefaults();
        }
    }

    private sealed record SettingsPayload(IReadOnlyList<SectionDto> Sections);

    private sealed record SectionDto(string Name, IReadOnlyDictionary<string, string> Pairs);
}
