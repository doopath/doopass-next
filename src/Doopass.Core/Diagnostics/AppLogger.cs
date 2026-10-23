namespace Doopass.Core.Diagnostics;

/// <summary>
/// Minimal file logger shared by the TUI and the web host. Writes append-only,
/// timestamped lines to the configured log file and rotates it at 5 MB.
/// Logging must never throw into application code.
/// </summary>
public static class AppLogger
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly object Gate = new();
    private static string? _file;

    /// <summary>Enables logging to the given file.</summary>
    public static void Initialize(string file)
    {
        lock (Gate)
        {
            _file = file;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        string? file;

        lock (Gate)
        {
            file = _file;
        }

        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                RotateIfNeeded(file);

                File.AppendAllText(
                    file,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} {level} {message}{Environment.NewLine}",
                    TextFile.Utf8NoBom);
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private static void RotateIfNeeded(string file)
    {
        try
        {
            var info = new FileInfo(file);

            if (!info.Exists || info.Length < MaxBytes)
            {
                return;
            }

            var archive = file + ".1";

            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            File.Move(file, archive);
        }
        catch
        {
            // Ignore rotation failures.
        }
    }
}
