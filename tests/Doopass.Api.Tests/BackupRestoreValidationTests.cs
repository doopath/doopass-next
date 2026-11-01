using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Doopass.Core.Configuration;
using Doopass.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// Restoring used to overwrite store.enc and only then try to decrypt the backup,
/// so a garbage file destroyed the live vault. The candidate is now validated
/// first and the store is left byte-identical on failure.
/// </summary>
public sealed class BackupRestoreValidationTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Garbage_backup_is_rejected_without_touching_the_store()
    {
        var (client, token, settings) = await SetUpAsync();

        await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token,
            VaultApiTestsHelpers.Json(new { key = "keep", value = "still here" }));

        var storeHash = StoreHash(settings);
        Directory.CreateDirectory(settings.BackupsDir);
        await File.WriteAllTextAsync(Path.Combine(settings.BackupsDir, "garbage.enc.bak"), "not a vault");
        var backupsBefore = await GetBackupNamesAsync(client, token);

        var restore = await client.AuthSend(HttpMethod.Post, "/api/backups/restore?name=garbage", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, restore.StatusCode);
        Assert.Contains("corrupted", await restore.Content.ReadAsStringAsync());

        Assert.Equal(storeHash, StoreHash(settings));
        Assert.Equal(backupsBefore, await GetBackupNamesAsync(client, token));
        Assert.Contains("keep", await (await client.AuthSend(HttpMethod.Get, "/api/vault/keys", token))
            .Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task T02_Backup_from_another_vault_is_rejected_without_touching_the_store()
    {
        var (client, token, settings) = await SetUpAsync();

        var storeHash = StoreHash(settings);
        Directory.CreateDirectory(settings.BackupsDir);
        await File.WriteAllTextAsync(
            Path.Combine(settings.BackupsDir, "othervault.enc.bak"),
            Store.Encrypt("a different master password", []));

        var restore = await client.AuthSend(HttpMethod.Post, "/api/backups/restore?name=othervault", token);

        Assert.Equal(HttpStatusCode.Unauthorized, restore.StatusCode);
        Assert.Equal(storeHash, StoreHash(settings));
    }

    private async Task<(HttpClient Client, string Token, Settings Settings)> SetUpAsync()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Restore test 1234");
        return (fixture.Client, token, fixture.App.Services.GetRequiredService<Settings>());
    }

    private static string StoreHash(Settings settings) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settings.StoreFile)));

    private static async Task<IReadOnlyList<string>> GetBackupNamesAsync(HttpClient client, string token)
    {
        var payload = await (await client.AuthSend(HttpMethod.Get, "/api/backups", token))
            .Content.ReadFromJsonAsync<BackupList>();
        return payload!.Names;
    }

    private sealed record BackupList(IReadOnlyList<string> Names);
}
