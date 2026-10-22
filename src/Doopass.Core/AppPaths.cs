namespace Doopass.Core;

/// <summary>
/// Provides the on-disk locations used by Doopass. The default root is
/// <c>~/doopass</c> (matching the original Python implementation) so that
/// existing <c>store.enc</c> and <c>settings.json</c> files are picked up
/// without migration. The root can be overridden (for tests / the web host)
/// via <see cref="OverrideRoot"/>.
/// </summary>
public static class AppPaths
{
    private static string? _overrideRoot;

    /// <summary>Overrides the application root directory (mainly for tests).</summary>
    public static void OverrideRoot(string path) => _overrideRoot = path;

    /// <summary>Clears any root override.</summary>
    public static void ResetOverride() => _overrideRoot = null;

    /// <summary>The user's home directory.</summary>
    public static string Home
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            }

            return home;
        }
    }

    /// <summary>The application root directory (<c>~/doopass</c> by default).</summary>
    public static string AppDir =>
        _overrideRoot
        ?? Environment.GetEnvironmentVariable("DOOPASS_HOME")
        ?? Path.Combine(Home, "doopass");

    /// <summary>The application data directory.</summary>
    public static string AppDataDir => Path.Combine(AppDir, "appdata");

    /// <summary>The settings file path.</summary>
    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");
}
