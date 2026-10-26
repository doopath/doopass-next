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
    public static void WriteAllText(string path, string contents, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        Directory.CreateDirectory(directory);

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
