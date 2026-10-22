namespace Doopass.Core.Diagnostics;

/// <summary>A minimal file logger used across the TUI and web host.</summary>
public static class AppLogger
{
    private static readonly object Gate = new();
    private static string? _file;

    /// <summary>Enables logging to the given file.</summary>
    public static void Initialize(string file) => _file = file;

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception exception) => Write("ERROR", $"{message} {exception}");

    private static void Write(string level, string message)
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    _file,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }
}
