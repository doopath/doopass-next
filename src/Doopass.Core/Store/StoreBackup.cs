using System.Text;
using Doopass.Core.Diagnostics;

namespace Doopass.Core.Storage;

/// <summary>
/// Creates, lists, restores and deletes dated backups of the encrypted store,
/// replicating the original Doopass backup naming scheme.
/// </summary>
public sealed class StoreBackup
{
    private readonly string _storePath;
    private readonly string _backupsDir;
    private readonly string _extension;

    public StoreBackup(string storePath, string backupsDir, string extension = Constants.StoreBackupExtension)
    {
        _storePath = storePath;
        _backupsDir = backupsDir;
        _extension = extension;

        if (!Directory.Exists(backupsDir))
        {
            Directory.CreateDirectory(backupsDir);
        }
    }

    /// <summary>Returns the raw (still encrypted) content of the store file.</summary>
    public string GetStoreContent() => File.ReadAllText(_storePath, Encoding.UTF8);

    /// <summary>Returns the names (without extension) of all backups, newest first.</summary>
    public IReadOnlyList<string> GetBackupNames()
    {
        if (!Directory.Exists(_backupsDir))
        {
            return [];
        }

        return Directory.GetFiles(_backupsDir)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name.EndsWith(_extension, StringComparison.Ordinal))
            .Select(name => name![..^_extension.Length])
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// True for a plain, extensionless backup name (letters, digits, '_', '-', '.').
    /// Rejects anything that could escape the backups directory.
    /// </summary>
    public static bool IsSafeBackupName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return false;
        }

        if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        // An array, not a collection expression: IndexOfAny has overloads for
        // both char[] and ReadOnlySpan<char>, so the compiler has no ambiguity.
        if (name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
        {
            return false;
        }

        return Path.GetFileName(name) == name;
    }

    /// <summary>Reads the raw content of a backup by name (including extension).</summary>
    public string GetBackupContent(string backupName) =>
        File.ReadAllText(ResolvePath(backupName), Encoding.UTF8);

    /// <summary>Creates a new dated backup of the current store.</summary>
    public void MakeBackup()
    {
        var currentDate = DateTime.Today.ToString("dd_MM_yyyy");
        var storeName = Path.GetFileName(_storePath);
        var storeBase = storeName.EndsWith(".enc", StringComparison.Ordinal)
            ? storeName[..^".enc".Length]
            : storeName;

        var basePath = Path.Combine(_backupsDir, $"{currentDate}_{storeBase}");

        var storeContent = GetStoreContent();
        var datedBackups = GetDatedBackups($"{currentDate}_{storeBase}");

        // Max+1, not "last sorted name + 1": ordinal sorting puts "_9" after "_10",
        // so the old scheme silently overwrote _10 on the eleventh backup of a day.
        var nextNumber = datedBackups.Count == 0
            ? 1
            : datedBackups.Max(ExtractBackupNumber) + 1;
        var backupPath = $"{basePath}_{nextNumber}{_extension}";

        AtomicFile.WriteAllText(backupPath, storeContent, TextFile.Utf8NoBom);
        AppLogger.Info($"Backup created: {Path.GetFileName(backupPath)}");
    }

    /// <summary>Replaces the current store with a backup (making a backup first).</summary>
    public void SwapStoreWithBackup(string backupName)
    {
        var backupContent = GetBackupContent(backupName);
        MakeBackup();

        AtomicFile.WriteAllText(_storePath, backupContent, TextFile.Utf8NoBom);
        AppLogger.Info($"Store restored from backup: {backupName}");
    }

    /// <summary>Deletes a backup by name (including extension).</summary>
    public void DeleteBackup(string backupName)
    {
        var path = ResolvePath(backupName);

        if (File.Exists(path))
        {
            File.Delete(path);
            AppLogger.Info($"Backup deleted: {backupName}");
        }
    }

    /// <summary>
    /// Resolves a backup file name inside the backups directory and rejects any
    /// name whose resolved path would leave it (defence in depth: TUI, web-UI and
    /// API all end up here). The error deliberately matches "no such backup" so
    /// a rejected path is indistinguishable from a missing one.
    /// </summary>
    private string ResolvePath(string fileName)
    {
        var root = Path.GetFullPath(_backupsDir);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, fileName));

        if (!full.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new PropertyDoesNotExistError($"Backup '{Path.GetFileName(fileName)}' does not exist!");
        }

        return full;
    }

    private IReadOnlyList<string> GetDatedBackups(string baseName)
    {
        if (!Directory.Exists(_backupsDir))
        {
            return [];
        }

        return Directory.GetFiles(_backupsDir)
            .Select(Path.GetFileName)
            .Where(name => name is not null
                           && name.StartsWith(baseName, StringComparison.Ordinal)
                           && name.EndsWith(_extension, StringComparison.Ordinal))
            .Select(name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();
    }

    private int ExtractBackupNumber(string backupName)
    {
        var withoutExtension = backupName.EndsWith(_extension, StringComparison.Ordinal)
            ? backupName[..^_extension.Length]
            : backupName;

        var lastUnderscore = withoutExtension.LastIndexOf('_');

        if (lastUnderscore >= 0
            && int.TryParse(withoutExtension[(lastUnderscore + 1)..], out var number))
        {
            return number;
        }

        return 0;
    }
}
