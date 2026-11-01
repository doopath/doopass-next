using Doopass.Core.Configuration;
using Xunit;

namespace Doopass.Core.Tests;

/// <summary>
/// The settings UI/API is not a file-system primitive: path settings are
/// read-only, unknown keys are rejected, and the one editable non-path setting
/// is format-checked. Path values can still be changed by hand in settings.json
/// while Doopass is stopped.
/// </summary>
[Collection(AppPathsCollection.Name)]
public sealed class SettingsPolicyTests : IDisposable
{
    private readonly string _root;

    public SettingsPolicyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "doopass-settings-policy", Guid.NewGuid().ToString("N"));
        AppPaths.OverrideRoot(_root);
    }

    public void Dispose()
    {
        AppPaths.ResetOverride();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Theory]
    [InlineData("store_file")]
    [InlineData("log_file")]
    [InlineData("backups")]
    [InlineData("default_config")]
    [InlineData("evil_key")]
    public void PathAndUnknownKeys_AreReadOnly(string key)
    {
        var settings = new Settings();

        Assert.False(Settings.IsEditable("local", key));
        Assert.Throws<ReadOnlySettingError>(() => settings.ModifyProperty("local", key, "/tmp/evil"));
    }

    [Fact]
    public void UnknownSection_IsReportedAsMissing()
    {
        var settings = new Settings();

        Assert.Throws<PropertyDoesNotExistError>(() => settings.ModifyProperty("nope", "anything", "1.0.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("../../x")]
    [InlineData("bak")]
    [InlineData(".enc.bak.extra.more")]
    [InlineData(".enc/bak")]
    public void BackupExtension_RejectsMalformedValues(string value)
    {
        var settings = new Settings();

        Assert.Throws<InvalidSettingValueError>(() => settings.ModifyProperty("local", "backup_extension", value));
        Assert.Equal(".enc.bak", settings.BackupExtension);
    }

    [Fact]
    public void BackupExtension_AcceptsAndPersistsValidValue()
    {
        var settings = new Settings();
        settings.ModifyProperty("local", "backup_extension", ".enc.bak2");

        var reloaded = new Settings();

        Assert.Equal(".enc.bak2", reloaded.BackupExtension);
    }

    [Fact]
    public void RestoreDefaults_KeepsTlsSettings()
    {
        var settings = new Settings();
        settings.SetTls("/tmp/doopass.pfx", "secret");

        settings.RestoreDefaults();

        var api = new Settings().GetSection("api");
        Assert.Equal("/tmp/doopass.pfx", api["tls_pfx"]);
        Assert.Equal("secret", api["tls_pass"]);
    }

    [Fact]
    public void Version_IsNoLongerEditable()
    {
        var settings = new Settings();

        Assert.False(Settings.IsEditable("local", "version"));
        Assert.DoesNotContain("version", settings.GetSection("local").Keys);
    }
}

/// <summary>Lets the settings tests share one collection: they mutate the global <see cref="AppPaths"/> root.</summary>
[CollectionDefinition(Name)]
public sealed class AppPathsCollection
{
    public const string Name = "app-paths";
}
