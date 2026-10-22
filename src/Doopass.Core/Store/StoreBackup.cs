using System.Text;

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

    /// <summary>Reads the raw content of a backup by name (including extension).</summary>
    public string GetBackupContent(string backupName) =>
        File.ReadAllText(Path.Combine(_backupsDir, backupName), Encoding.UTF8);

    /// <summary>Creates a new dated backup of the current store.</summary>
    public void MakeBackup()
    {
        var currentDate = DateTime.Today.ToString("dd_MM_yyyy");
        var storeName = Path.GetFileName(_storePath);
        var storeBase = storeName.EndsWith(".enc", StringComparison.Ordinal)
            ? storeName[..^".enc".Length]
            : storeName;

        var basePath = Path.Combine(_backupsDir, $"{currentDate}_{storeBase}");
        var backupPath = $"{basePath}_1{_extension}";

        var storeContent = GetStoreContent();
        var datedBackups = GetDatedBackups($"{currentDate}_{storeBase}");

        if (datedBackups.Count > 0)
        {
            var lastNumber = ExtractBackupNumber(datedBackups[^1]);
            backupPath = $"{basePath}_{lastNumber + 1}{_extension}";
        }

        File.WriteAllText(backupPath, storeContent, Encoding.UTF8);
    }

    /// <summary>Replaces the current store with a backup (making a backup first).</summary>
    public void SwapStoreWithBackup(string backupName)
    {
        var backupContent = GetBackupContent(backupName);
        MakeBackup();

        File.WriteAllText(_storePath, backupContent, Encoding.UTF8);
    }

    /// <summary>Deletes a backup by name (including extension).</summary>
    public void DeleteBackup(string backupName)
    {
        var path = Path.Combine(_backupsDir, backupName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
