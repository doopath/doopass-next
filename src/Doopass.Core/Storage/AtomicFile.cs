using System.Text;
using Doopass.Core.Diagnostics;

namespace Doopass.Core.Storage;

/// <summary>
/// Writes a text file atomically: the content goes to a temp file in the same
/// directory and is then renamed over the target. A crash, a full disk or a
/// parallel reader can therefore never observe a half-written store.enc or
/// settings.json (the old content stays visible until the rename).
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/> via a temp file and a rename.</summary>
    /// <param name="path">Target file path.</param>
    /// <param name="contents">Text to write.</param>
    /// <param name="encoding">Encoding used for the temp file.</param>
    public static void WriteAllText(string path, string contents, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        Directory.CreateDirectory(directory);
        SweepStaleTempFiles(directory, Path.GetFileName(path));

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temp, contents, encoding);
            CopyUnixMode(path, temp);

            // rename(2) on Unix, MoveFileEx(REPLACE_EXISTING) on Windows.
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException ex)
                {
                    AppLogger.Debug($"Could not remove temp file '{temp}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Removes temp files a crashed or killed process left behind for the same
    /// target an hour ago. Best effort: a cleanup failure must never fail the
    /// write that follows.
    /// </summary>
    private static void SweepStaleTempFiles(string directory, string fileName)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);

        try
        {
            foreach (var stale in Directory.EnumerateFiles(directory, $".{fileName}.*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(stale) < cutoff)
                    {
                        File.Delete(stale);
                        AppLogger.Debug($"Removed stale temp file '{stale}'.");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLogger.Debug($"Could not remove stale temp file '{stale}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Debug($"Could not sweep stale temp files in '{directory}': {ex.Message}");
        }
    }

    /// <summary>Keeps the previous permissions (a fresh file would be 0644, exposing a 0600 store).</summary>
    private static void CopyUnixMode(string existing, string replacement)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(existing))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(replacement, File.GetUnixFileMode(existing));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Debug($"Could not preserve file mode for '{existing}': {ex.Message}");
        }
    }
}
