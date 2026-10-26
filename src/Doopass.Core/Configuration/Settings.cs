using System.Text.Json;
using System.Text.RegularExpressions;
using Doopass.Core.Diagnostics;
using Doopass.Core.Storage;

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

    /// <summary>
    /// Sections that older versions wrote to <c>settings.json</c> but that are no
    /// longer part of the configuration. They are dropped on load so the settings
    /// UI never shows them again.
    /// </summary>
    private static readonly HashSet<string> ObsoleteSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "remote",
    };

    /// <summary>
    /// Settings the UI/API may change. Everything that names a path is read-only:
    /// a writable <c>store_file</c> or <c>backup_extension</c> turns the settings
    /// API into an arbitrary-file write (or delete) primitive.
    /// </summary>
    private static readonly HashSet<string> EditableKeys = new(StringComparer.Ordinal)
    {
        "version",            // informational
        "backup_extension",   // validated below
    };

    private static readonly Regex BackupExtensionPattern =
        new(@"^(\.[A-Za-z0-9]{1,8}){1,2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    /// <summary>The names of all top-level settings sections (currently only <c>local</c>).</summary>
    public IReadOnlyList<string> Sections => _settings.Keys.ToList();

    /// <summary>Returns a substituted value from a section.</summary>
    public string GetValue(string section, string key) => _settings[section][key];

    /// <summary>Returns all key/value pairs in a section (with substituted paths).</summary>
    public IReadOnlyDictionary<string, string> GetSection(string section) =>
        new Dictionary<string, string>(_settings[section]);

    /// <summary>True when the UI/API may change this setting; paths are read-only.</summary>
    public static bool IsEditable(string section, string key) =>
        string.Equals(section, "local", StringComparison.Ordinal) && EditableKeys.Contains(key);

    /// <summary>Changes a single setting and persists it.</summary>
    public void ModifyProperty(string section, string key, string value)
    {
        if (!_settings.ContainsKey(section))
        {
            throw new PropertyDoesNotExistError($"Settings section '{section}' does not exist!");
        }

        if (!IsEditable(section, key))
        {
            throw new ReadOnlySettingError($"Setting '{section}.{key}' is read-only.");
        }

        if (key == "backup_extension" && !BackupExtensionPattern.IsMatch(value))
        {
            throw new InvalidSettingValueError(
                "backup_extension must look like '.enc.bak' (one or two dot-separated alphanumerics).");
        }

        _settings[section][key] = value;
        Save();
        AppLogger.Info($"Setting changed: {section}.{key}");
    }

    /// <summary>Restores the embedded default configuration.</summary>
    public void RestoreDefaults()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(AppDataDir);

        AtomicFile.WriteAllText(SettingsFile, ReadEmbeddedDefault(), TextFile.Utf8NoBom);

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

        var droppedObsolete = false;

        foreach (var (section, pairs) in raw)
        {
            if (ObsoleteSections.Contains(section))
            {
                droppedObsolete = true;
                AppLogger.Info($"Dropped obsolete settings section: {section}");
                continue;
            }

            var substituted = new Dictionary<string, string>();

            foreach (var (key, value) in pairs)
            {
                substituted[key] = SubstituteAppData(value);
            }

            _settings[section] = substituted;
        }

        if (droppedObsolete)
        {
            Save();
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
        AtomicFile.WriteAllText(SettingsFile, json, TextFile.Utf8NoBom);
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
