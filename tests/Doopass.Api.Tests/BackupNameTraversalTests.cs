using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Doopass.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The backup-name PoC from the review: a token holder must not be able to delete
/// or restore files outside the backups directory by passing a rooted or ".."
/// name. Every escape attempt has to look exactly like a missing backup and leave
/// both the victim file and store.enc untouched.
/// </summary>
public sealed class BackupNameTraversalTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    private static readonly SemaphoreSlim InitGate = new(1, 1);
    private static ApiHostFixture? _initialized;
    private static string _token = string.Empty;

    public static TheoryData<string> UnsafeNames => new()
    {
        "../../outside",
        "..\\outside",
        "/tmp/outside",
        "C:\\outside",
        "..",
        ".",
        "a/b",
        "a\\b",
        "",
        "x:y",
    };

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public async Task T01_Restore_and_delete_reject_unsafe_names(string name)
    {
        var token = await TokenAsync();
        var settings = Settings();
        var escaped = Uri.EscapeDataString(name);

        var victim = CreateWouldBeVictim(settings, name);
        var storeHashBefore = StoreHash(settings);

        var delete = await fixture.Client.AuthSend(HttpMethod.Delete, $"/api/backups?name={escaped}", token);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        await AssertMissingBackupError(delete, name);

        var restore = await fixture.Client.AuthSend(HttpMethod.Post, $"/api/backups/restore?name={escaped}", token);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
        await AssertMissingBackupError(restore, name);

        if (victim is not null)
        {
            Assert.True(File.Exists(victim), $"victim file must survive: {victim}");
            Assert.Equal("USER DATA", await File.ReadAllTextAsync(victim));
        }

        Assert.Equal(storeHashBefore, StoreHash(settings));
        Assert.DoesNotContain("Exception", await restore.Content.ReadAsStringAsync());
    }

    /// <summary>Valid names still list/restore/delete (the full cycle lives in VaultDataTests.T02).</summary>
    [Fact]
    public async Task T02_Valid_backup_name_still_round_trips()
    {
        var token = await TokenAsync();
        var client = fixture.Client;

        var create = await client.AuthSend(HttpMethod.Post, "/api/backups", token);
        Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);

        var names = (await (await client.AuthSend(HttpMethod.Get, "/api/backups", token)).Content
            .ReadFromJsonAsync<BackupList>())!.Names;
        var name = names[0];

        var restore = await client.AuthSend(HttpMethod.Post, $"/api/backups/restore?name={Uri.EscapeDataString(name)}", token);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        var delete = await client.AuthSend(HttpMethod.Delete, $"/api/backups?name={Uri.EscapeDataString(name)}", token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    private Settings Settings() => fixture.App.Services.GetRequiredService<Settings>();

    /// <summary>
    /// Creates the file a traversal name would hit if the name were honoured, so
    /// the test can prove it was never touched. Names that resolve inside the
    /// backups directory have no victim.
    /// </summary>
    private static string? CreateWouldBeVictim(Settings settings, string name)
    {
        var resolved = Path.GetFullPath(Path.Combine(settings.BackupsDir, name + settings.BackupExtension));
        var root = Path.GetFullPath(settings.BackupsDir) + Path.DirectorySeparatorChar;

        if (resolved.StartsWith(root, StringComparison.Ordinal))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(resolved);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(resolved, "USER DATA");
        return resolved;
    }

    private static string StoreHash(Settings settings) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settings.StoreFile)));

    private static async Task AssertMissingBackupError(HttpResponseMessage response, string name)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error").GetString();
        Assert.Equal($"Backup '{name}' does not exist!", error);
    }

    private async Task<string> TokenAsync()
    {
        // One init per fixture: /api/auth/* is rate limited to 5 attempts/minute,
        // so the theory cases share a single token instead of logging in each time.
        await InitGate.WaitAsync();

        try
        {
            if (!ReferenceEquals(_initialized, fixture))
            {
                fixture.Reset();
                var (token, _) = await fixture.InitAsync("Traversal test 1234");
                _token = token;
                _initialized = fixture;
            }

            return _token;
        }
        finally
        {
            InitGate.Release();
        }
    }

    private sealed record BackupList(IReadOnlyList<string> Names);
}
