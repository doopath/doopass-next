using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

/// <summary>
/// Backup names come from the user (and, through the API, from the network), so
/// anything that could escape the backups directory must be rejected before it
/// reaches the file system.
/// </summary>
public sealed class BackupTests : IDisposable
{
    private readonly string _dir;
    private readonly string _backupsDir;
    private readonly string _storePath;
    private readonly StoreBackup _backups;

    public BackupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "doopass-tests", Guid.NewGuid().ToString("N"));
        _backupsDir = Path.Combine(_dir, "backups");
        _storePath = Path.Combine(_dir, "store.enc");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_storePath, "ciphertext");

        _backups = new StoreBackup(_storePath, _backupsDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Theory]
    [InlineData("12_10_2025_store_1")]
    [InlineData("2025-10-12_store.enc_7")]
    [InlineData("a")]
    [InlineData("_1")]
    [InlineData("backup with spaces")]
    public void IsSafeBackupName_AcceptsGeneratedNames(string name)
    {
        Assert.True(StoreBackup.IsSafeBackupName(name));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("..\\outside")]
    [InlineData("/tmp/outside")]
    [InlineData("C:\\outside")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x:y")]
    [InlineData("null\0byte")]
    public void IsSafeBackupName_RejectsTraversal(string name)
    {
        Assert.False(StoreBackup.IsSafeBackupName(name));
    }

    [Fact]
    public void IsSafeBackupName_RejectsNullAndOverlongNames()
    {
        Assert.False(StoreBackup.IsSafeBackupName(null));
        Assert.False(StoreBackup.IsSafeBackupName(new string('a', 256)));
    }

    [Fact]
    public void DeleteBackup_RejectsPathTraversal()
    {
        Assert.Throws<PropertyDoesNotExistError>(() => _backups.DeleteBackup("../../x.enc.bak"));
    }

    [Fact]
    public void GetBackupContent_RejectsPathTraversal()
    {
        Assert.Throws<PropertyDoesNotExistError>(() => _backups.GetBackupContent("../../x.enc.bak"));
    }

    [Fact]
    public void DeleteBackup_KeepsFilesOutsideTheBackupsDirectory()
    {
        var victim = Path.Combine(_dir, "victim.enc.bak");
        File.WriteAllText(victim, "USER DATA");

        Assert.Throws<PropertyDoesNotExistError>(() => _backups.DeleteBackup($"../{Path.GetFileName(victim)}"));
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void ValidBackupLifecycle_StillWorks()
    {
        _backups.MakeBackup();

        var name = Assert.Single(_backups.GetBackupNames());
        Assert.Equal("ciphertext", _backups.GetBackupContent(name + ".enc.bak"));

        _backups.DeleteBackup(name + ".enc.bak");
        Assert.Empty(_backups.GetBackupNames());
    }

    [Fact]
    public void Twelve_backups_get_numbers_one_to_twelve()
    {
        for (var i = 0; i < 12; i++)
        {
            _backups.MakeBackup();
        }

        var names = _backups.GetBackupNames();

        Assert.Equal(12, names.Count);
        Assert.Equal(Enumerable.Range(1, 12), names.Select(BackupNumber).OrderBy(number => number));
    }

    [Fact]
    public void Number_continues_after_a_deleted_backup()
    {
        for (var i = 0; i < 12; i++)
        {
            _backups.MakeBackup();
        }

        var fifth = _backups.GetBackupNames().Single(name => BackupNumber(name) == 5);
        _backups.DeleteBackup(fifth + ".enc.bak");

        _backups.MakeBackup();

        var names = _backups.GetBackupNames();
        Assert.Equal(12, names.Count);
        Assert.Contains(names, name => BackupNumber(name) == 13);
    }

    [Fact]
    public void Existing_backups_are_never_overwritten()
    {
        File.WriteAllText(_storePath, "first");
        _backups.MakeBackup();

        File.WriteAllText(_storePath, "second");
        _backups.MakeBackup();

        var names = _backups.GetBackupNames()
            .ToDictionary(BackupNumber, name => name);

        Assert.Equal("first", _backups.GetBackupContent(names[1] + ".enc.bak"));
        Assert.Equal("second", _backups.GetBackupContent(names[2] + ".enc.bak"));
    }

    private static int BackupNumber(string name) =>
        int.Parse(name[(name.LastIndexOf('_') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
}
