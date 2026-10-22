using Doopass.Core.Configuration;
using Xunit;

namespace Doopass.Core.Tests;

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
    public void Sections_ContainLocalAndRemote()
    {
        var settings = new Settings();

        Assert.Contains("local", settings.Sections);
        Assert.Contains("remote", settings.Sections);
    }

    [Fact]
    public void ModifyProperty_PersistsAcrossReloads()
    {
        var settings = new Settings();
        settings.ModifyProperty("local", "version", "9.9.9");

        var reloaded = new Settings();

        Assert.Equal("9.9.9", reloaded.GetValue("local", "version"));
    }

    [Fact]
    public void RestoreDefaults_ResetsModifiedValues()
    {
        var settings = new Settings();
        settings.ModifyProperty("local", "version", "9.9.9");

        settings.RestoreDefaults();

        Assert.Equal(Constants.Version, settings.GetValue("local", "version"));
    }

    [Fact]
    public void ModifyPathProperty_PersistsAsPlaceholder()
    {
        var settings = new Settings();
        var custom = Path.Combine(settings.AppDataDir, "custom.enc");
        settings.ModifyProperty("local", "store_file", custom);

        var reloaded = new Settings();

        Assert.Equal(custom, reloaded.StoreFile);
    }
}
