using Doopass.Core.Configuration;
using Xunit;

namespace Doopass.Core.Tests;

[Collection(AppPathsCollection.Name)]
public sealed class SettingsTests : IDisposable
{
    private readonly string _root;

    public SettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "doopass-settings-tests", Guid.NewGuid().ToString("N"));
        AppPaths.OverrideRoot(_root);
    }

    public void Dispose()
    {
        AppPaths.ResetOverride();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void FirstRun_CreatesSettingsFile()
    {
        _ = new Settings();

        Assert.True(File.Exists(Path.Combine(_root, "appdata", "settings.json")));
    }

    [Fact]
    public void DefaultPaths_ResolveToAppDataDirectory()
    {
        var settings = new Settings();
        var appData = Path.Combine(_root, "appdata");

        Assert.Equal(Path.Combine(appData, "store.enc"), settings.StoreFile);
        Assert.Equal(Path.Combine(appData, "backups"), settings.BackupsDir);
        Assert.Equal(".enc.bak", settings.BackupExtension);
    }

    [Fact]
    public void SettingsFile_UsesAppDataPlaceholder()
    {
        _ = new Settings();

        var content = File.ReadAllText(Path.Combine(_root, "appdata", "settings.json"));

        Assert.Contains("APPDATA/store.enc", content);
        Assert.DoesNotContain(_root, content);
    }

    [Fact]
    public void Sections_ContainLocalOnly()
    {
        var settings = new Settings();

        Assert.Equal(["local"], settings.Sections);
    }

    [Fact]
    public void LegacyRemoteSection_IsDroppedOnLoad()
    {
        var settings = new Settings();
        var path = settings.SettingsFile;

        File.WriteAllText(path,
            """
            {
              "local": { "version": "5.0.0" },
              "remote": { "repository_url": "http://0.0.0.0:7240", "token": "no token" }
            }
            """);

        var reloaded = new Settings();

        Assert.Equal(["local"], reloaded.Sections);
        Assert.DoesNotContain("remote", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModifyProperty_PersistsAcrossReloads()
    {
        var settings = new Settings();
        settings.ModifyProperty("local", "backup_extension", ".enc.bak2");

        var reloaded = new Settings();

        Assert.Equal(".enc.bak2", reloaded.GetValue("local", "backup_extension"));
    }

    [Fact]
    public void RestoreDefaults_ResetsModifiedValues()
    {
        var settings = new Settings();
        settings.ModifyProperty("local", "backup_extension", ".enc.bak2");

        settings.RestoreDefaults();

        Assert.Equal(".enc.bak", settings.GetValue("local", "backup_extension"));
    }

    [Fact]
    public void LegacyVersionSetting_IsDroppedOnLoad()
    {
        var settings = new Settings();
        var path = settings.SettingsFile;

        File.WriteAllText(path,
            """
            {
              "local": { "version": "5.0.0", "backup_extension": ".enc.bak" }
            }
            """);

        var reloaded = new Settings();

        Assert.DoesNotContain("version", reloaded.GetSection("local").Keys);
        Assert.DoesNotContain("version", File.ReadAllText(path));
    }

    /// <summary>
    /// Path settings are read-only through the API/UI, but a hand-edited
    /// settings.json (Doopass stopped) must still round-trip its APPDATA placeholder.
    /// </summary>
    [Fact]
    public void ModifyPathProperty_PersistsAsPlaceholder()
    {
        var settings = new Settings();
        File.WriteAllText(settings.SettingsFile,
            """
            {
              "local": {
                "store_file": "APPDATA/custom.enc"
              }
            }
            """);

        var reloaded = new Settings();

        Assert.Equal(Path.Combine(reloaded.AppDataDir, "custom.enc"), reloaded.StoreFile);
    }

    [Fact]
    public void SettingsFile_HasNoUtf8Bom()
    {
        var settings = new Settings();
        settings.ModifyProperty("local", "backup_extension", ".enc.bak2");

        var bytes = File.ReadAllBytes(settings.SettingsFile);

        Assert.False(StartsWithBom(bytes));
    }

    [Fact]
    public void SettingsFile_WithBom_StillLoads()
    {
        var settings = new Settings();
        var path = settings.SettingsFile;
        var original = File.ReadAllBytes(path);

        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(original).ToArray());

        var reloaded = new Settings();

        Assert.Contains("local", reloaded.Sections);
    }

    private static bool StartsWithBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
