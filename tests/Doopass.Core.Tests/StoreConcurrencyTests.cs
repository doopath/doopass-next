using System.Collections.Concurrent;
using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

/// <summary>
/// The web host, the TUI and a second process can all hold a Store over the same
/// file. A mutation must be applied to the current on-disk state under a lock, and
/// the file must never be rewritten in place (temp + rename instead).
/// </summary>
public sealed class StoreConcurrencyTests : IDisposable
{
    private readonly string _dir;
    private readonly string _storePath;

    public StoreConcurrencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "doopass-concurrency-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _storePath = Path.Combine(_dir, "store.enc");
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
    public void Two_instances_do_not_lose_updates()
    {
        var a = new Store("secret", _storePath);
        var b = new Store("secret", _storePath);

        a.AddProperty("from-a", "1");
        b.AddProperty("from-b", "2");

        var keys = new Store("secret", _storePath).GetKeys();

        Assert.Equal(["from-a", "from-b"], keys);
    }

    [Fact]
    public void Stale_instance_sees_external_changes()
    {
        var a = new Store("secret", _storePath);
        var b = new Store("secret", _storePath);

        b.AddProperty("from-b", "2");

        Assert.Equal(["from-b"], a.GetKeys());
        Assert.Equal("2", a.GetValue("from-b"));
    }

    [Fact]
    public void Parallel_mutations_all_present()
    {
        var a = new Store("secret", _storePath);
        var b = new Store("secret", _storePath);
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 20, i =>
        {
            try
            {
                (i % 2 == 0 ? a : b).AddProperty($"key{i}", $"value{i}");
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        Assert.Empty(failures);

        var keys = new Store("secret", _storePath).GetKeys();

        Assert.Equal(20, keys.Count);
        Assert.Equal(20, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Deleted_store_file_reports_not_initialized()
    {
        var store = new Store("secret", _storePath);
        File.Delete(_storePath);

        Assert.Throws<StoreIsNotInitializedError>(store.GetKeys);
        Assert.Throws<StoreIsNotInitializedError>(() => store.AddProperty("x", "y"));
    }

    [Fact]
    public void Atomic_write_leaves_no_temp_files()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("one", "1");
        store.SetValue("one", "2");
        store.RemoveProperty("one");

        var leftovers = Directory.GetFiles(_dir)
            .Where(path => path.EndsWith(".tmp", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(leftovers);
        Assert.Empty(new Store("secret", _storePath).GetKeys());
    }

    [Fact]
    public void External_replace_is_picked_up()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("kept", "1");

        // A safety backup / restore in another session replaces the whole file.
        var replacement = Store.Encrypt("secret", System.Text.Encoding.UTF8.GetBytes("replaced = yes"));
        File.WriteAllText(_storePath, replacement);

        Assert.Equal(["replaced"], store.GetKeys());
    }
}
