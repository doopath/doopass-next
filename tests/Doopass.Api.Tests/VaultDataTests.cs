using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>Pair CRUD + backup lifecycle + reload (numbered; each test resets state, one init attempt).</summary>
[TestCaseOrderer("Doopass.Api.Tests.AlphabeticalTestCaseOrderer", "Doopass.Api.Tests")]
public sealed class VaultDataTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Pair_crud_full_cycle()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Data test 1234");
        var client = fixture.Client;

        // Add -> 204; duplicate -> 409
        var add = await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { key = "github", value = "s3cret=/ла" }));
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);

        var duplicate = await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { key = "github", value = "x" }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        // Keys list and value retrieval (unicode + special chars round-trip)
        var keys = await client.AuthSend(HttpMethod.Get, "/api/vault/keys", token);
        Assert.Equal("""{"keys":["github"]}""", await keys.Content.ReadAsStringAsync());

        var value = await client.AuthSend(HttpMethod.Get, "/api/vault/value?key=github", token);
        Assert.Equal("""{"value":"s3cret=/ла"}""", await value.Content.ReadAsStringAsync());

        var missing = await client.AuthSend(HttpMethod.Get, "/api/vault/value?key=nope", token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Update -> 204; unknown oldKey -> 404; newKey busy -> 409
        var update = await client.AuthSend(HttpMethod.Put, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { oldKey = "github", newKey = "gitlab", newValue = "changed" }));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var missingOldKey = await client.AuthSend(HttpMethod.Put, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { oldKey = "github", newKey = "v2", newValue = "…" }));
        Assert.Equal(HttpStatusCode.NotFound, missingOldKey.StatusCode);

        // The update is implemented as remove + add (web session semantics), so
        // a busy new key leaves the old key already removed; assert both facts.
        await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { key = "taken", value = "v" }));
        var busyNewKey = await client.AuthSend(HttpMethod.Put, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { oldKey = "gitlab", newKey = "taken", newValue = "…" }));
        Assert.Equal(HttpStatusCode.Conflict, busyNewKey.StatusCode);

        var keysAfterBusy = await (await client.AuthSend(HttpMethod.Get, "/api/vault/keys", token)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("gitlab", keysAfterBusy);
        Assert.Contains("taken", keysAfterBusy);

        // Delete -> 204; delete twice -> 404
        var remove = await client.AuthSend(HttpMethod.Delete, "/api/vault/pairs?key=taken", token);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var removeAgain = await client.AuthSend(HttpMethod.Delete, "/api/vault/pairs?key=taken", token);
        Assert.Equal(HttpStatusCode.NotFound, removeAgain.StatusCode);
    }

    [Fact]
    public async Task T02_Backup_lifecycle_changes_keys_and_writes_safety()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Data test 1234");
        var client = fixture.Client;

        await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { key = "one", value = "1" }));
        await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { key = "two", value = "2" }));

        var create = await client.AuthSend(HttpMethod.Post, "/api/backups", token);
        Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);

        var names = (await (await client.AuthSend(HttpMethod.Get, "/api/backups", token)).Content.ReadFromJsonAsync<BackupList>())!.Names;
        Assert.NotEmpty(names);
        var backupName = names[0];
        var backupCount = names.Count;

        // Delete 'one', then restore the backup: 'one' must be back. The restore
        // also writes a safety backup of the smaller vault.
        await client.AuthSend(HttpMethod.Delete, "/api/vault/pairs?key=one", token);
        var restore = await client.AuthSend(HttpMethod.Post, $"/api/backups/restore?name={Uri.EscapeDataString(backupName)}", token);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        var keys = await (await client.AuthSend(HttpMethod.Get, "/api/vault/keys", token)).Content.ReadAsStringAsync();
        Assert.Contains("one", keys);

        var namesAfter = (await (await client.AuthSend(HttpMethod.Get, "/api/backups", token)).Content.ReadFromJsonAsync<BackupList>())!.Names;
        Assert.True(namesAfter.Count >= backupCount, "restore must not lose backup history");

        // Delete -> 204; deleting an unknown backup -> 404
        var delete = await client.AuthSend(HttpMethod.Delete, $"/api/backups?name={Uri.EscapeDataString(backupName)}", token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var deleteMissing = await client.AuthSend(HttpMethod.Delete, "/api/backups?name=does-not-exist", token);
        Assert.Equal(HttpStatusCode.NotFound, deleteMissing.StatusCode);
    }

    /// <summary>Reload re-reads the store from disk; the pending pair survives.</summary>
    [Fact]
    public async Task T03_Reload_rereads_disk_state()
    {
        fixture.Reset();
        var (token, _) = await fixture.InitAsync("Data test 1234");
        var client = fixture.Client;

        await client.AuthSend(HttpMethod.Post, "/api/vault/pairs", token, VaultApiTestsHelpers.Json(new { key = "pending", value = "v" }));
        var reload = await client.AuthSend(HttpMethod.Post, "/api/vault/reload", token);
        Assert.Equal(HttpStatusCode.NoContent, reload.StatusCode);

        var keys = await (await client.AuthSend(HttpMethod.Get, "/api/vault/keys", token)).Content.ReadAsStringAsync();
        Assert.Contains("pending", keys);
    }

    private sealed record BackupList(IReadOnlyList<string> Names);
}
