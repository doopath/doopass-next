using System.Text.Json;
using Doopass.Core.Diagnostics;

namespace Doopass.Core.Configuration;

/// <summary>
/// Loads, saves and edits <c>settings.json</c>. The on-disk format mirrors the
/// original Doopass, using an <c>APPDATA/...</c> placeholder for paths so that
/// settings remain portable across machines. Defaults are embedded in the
/// assembly (no network access required).
/// </summary>
public sealed class Settings
{
    private const string DefaultConfigResource = "Doopass.Core.Configuration.DefaultConfig.json";

    private Dictionary<string, Dictionary<string, string>> _settings = new();

    public Settings()
    {
        AppDir = AppPaths.AppDir;
        AppDataDir = AppPaths.AppDataDir;
        SettingsFile = AppPaths.SettingsFile;

        Initialize();
    }

    public string AppDir { get; }

    public string AppDataDir { get; }

    public string SettingsFile { get; }

    /// <summary>The resolved path to the encrypted store file.</summary>
    public string StoreFile => GetValue("local", "store_file");

    /// <summary>The resolved path to the log file.</summary>
    public string LogFile => GetValue("local", "log_file");

    /// <summary>The resolved directory used for store backups.</summary>
    public string BackupsDir => GetValue("local", "backups");

    /// <summary>The backup file extension.</summary>
    public string BackupExtension => GetValue("local", "backup_extension");

    /// <summary>The names of all top-level settings sections (<c>local</c>, <c>remote</c>).</summary>
    public IReadOnlyList<string> Sections => _settings.Keys.ToList();

    /// <summary>Returns a substituted value from a section.</summary>
    public string GetValue(string section, string key) => _settings[section][key];

    /// <summary>Returns all key/value pairs in a section (with substituted paths).</summary>
    public IReadOnlyDictionary<string, string> GetSection(string section) =>
        new Dictionary<string, string>(_settings[section]);

    /// <summary>Changes a single setting and persists it.</summary>
    public void ModifyProperty(string section, string key, string value)
    {
        if (!_settings.TryGetValue(section, out var pairs))
        {
            pairs = new Dictionary<string, string>();
            _settings[section] = pairs;
        }

        pairs[key] = value;
        Save();
        AppLogger.Info($"Setting changed: {section}.{key}");
    }

    /// <summary>Restores the embedded default configuration.</summary>
    public void RestoreDefaults()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(AppDataDir);

        File.WriteAllText(SettingsFile, ReadEmbeddedDefault(), TextFile.Utf8NoBom);

        Load();
        AppLogger.Info("Settings restored to defaults");
    }

    private void Initialize()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(AppDataDir);

        if (!File.Exists(SettingsFile))
        {
            RestoreDefaults();
            return;
        }

        Load();
    }

    private void Load()
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            File.ReadAllText(SettingsFile, System.Text.Encoding.UTF8));

        _settings = new Dictionary<string, Dictionary<string, string>>();

        if (raw is null)
        {
            return;
        }

        foreach (var (section, pairs) in raw)
        {
            var substituted = new Dictionary<string, string>();

            foreach (var (key, value) in pairs)
            {
                substituted[key] = SubstituteAppData(value);
            }

            _settings[section] = substituted;
        }
    }

    private void Save()
    {
        var raw = new Dictionary<string, Dictionary<string, string>>();

        foreach (var (section, pairs) in _settings)
        {
            var unsubstituted = new Dictionary<string, string>();

            foreach (var (key, value) in pairs)
            {
                unsubstituted[key] = UnsubstituteAppData(value);
            }

            raw[section] = unsubstituted;
        }

        var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json, TextFile.Utf8NoBom);
    }

    private string SubstituteAppData(string value)
    {
        if (!value.Contains("APPDATA", StringComparison.Ordinal))
        {
            return value;
        }

        var index = value.IndexOf("APPDATA", StringComparison.Ordinal);
        var relative = value[(index + "APPDATA".Length)..].TrimStart('/', '\\');

        return Path.Combine(AppDataDir, relative);
    }

    private string UnsubstituteAppData(string value)
    {
        if (value.StartsWith(AppDataDir, StringComparison.Ordinal))
        {
            var relative = value[AppDataDir.Length..].TrimStart('\\', '/');
            return "APPDATA/" + relative;
        }

        return value;
    }

    private static string ReadEmbeddedDefault()
    {
        using var stream = typeof(Settings).Assembly.GetManifestResourceStream(DefaultConfigResource)
            ?? throw new InvalidOperationException($"Embedded resource '{DefaultConfigResource}' not found.");

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
