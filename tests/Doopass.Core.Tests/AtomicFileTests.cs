using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

/// <summary>
/// A crashed atomic write leaves a <c>.&lt;file&gt;.*.tmp</c> sibling behind.
/// Rewriting the same target later should clean up the old debris, but never a
/// temp file another writer is using right now.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _dir;
    private readonly string _target;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "doopass-atomic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _target = Path.Combine(_dir, "store.enc");
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

    [Fact]
    public void Stale_temp_files_are_swept_but_fresh_ones_are_kept()
    {
        var stale = TempName();
        var fresh = TempName();
        File.WriteAllText(stale, "orphaned by a crash");
        File.WriteAllText(fresh, "another writer is still working");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        AtomicFile.WriteAllText(_target, "new content", Doopass.Core.TextFile.Utf8NoBom);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.Equal("new content", File.ReadAllText(_target));
    }

    [Fact]
    public void Temp_files_of_other_targets_are_left_alone()
    {
        var other = Path.Combine(_dir, ".settings.json.0123456789abcdef.tmp");
        File.WriteAllText(other, "other target");
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddHours(-2));

        AtomicFile.WriteAllText(_target, "new content", Doopass.Core.TextFile.Utf8NoBom);

        Assert.True(File.Exists(other));
    }

    private string TempName() =>
        Path.Combine(_dir, $".{Path.GetFileName(_target)}.{Guid.NewGuid():N}.tmp");
}
