using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _storePath;

    public StoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "doopass-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _storePath = Path.Combine(_dir, "store.enc");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void NewStore_CreatesEncryptedFile()
    {
        var store = new Store("secret", _storePath);

        Assert.True(File.Exists(_storePath));
        Assert.Empty(store.GetKeys());
    }

    [Fact]
    public void AddAndReload_PersistsEntries()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("github", "token123");
        store.AddProperty("email", "a@b.c");

        var reopened = new Store("secret", _storePath);

        Assert.Equal(["github", "email"], reopened.GetKeys());
        Assert.Equal("token123", reopened.GetValue("github"));
        Assert.Equal("a@b.c", reopened.GetValue("email"));
    }

    [Fact]
    public void AddDuplicate_Throws()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("github", "token123");

        Assert.Throws<PropertyAlreadyExistError>(() => store.AddProperty("github", "other"));
    }

    [Fact]
    public void SetValue_UpdatesExistingValue()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("github", "token123");
        store.SetValue("github", "token456");

        Assert.Equal("token456", store.GetValue("github"));
    }

    [Fact]
    public void RemoveProperty_RemovesEntry()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("github", "token123");
        store.AddProperty("email", "a@b.c");

        store.RemoveProperty("github");

        Assert.False(store.DoesPropertyExist("github"));
        Assert.Equal(["email"], store.GetKeys());
    }

    [Fact]
    public void GetValue_Missing_Throws()
    {
        var store = new Store("secret", _storePath);

        Assert.Throws<PropertyDoesNotExistError>(() => store.GetValue("missing"));
    }

    [Fact]
    public void ValueContainingEquals_IsPreserved()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("connection", "user=admin;password=a=b=c");

        var reopened = new Store("secret", _storePath);

        Assert.Equal("user=admin;password=a=b=c", reopened.GetValue("connection"));
    }

    [Fact]
    public void TryInitializeStore_WithWrongPassword_ReturnsNull()
    {
        _ = new Store("secret", _storePath);

        var result = Store.TryInitializeStore("wrong", _storePath);

        Assert.Null(result);
    }

    [Fact]
    public void TryInitializeExistingStore_WhenMissing_Throws()
    {
        Assert.Throws<StoreIsNotInitializedError>(
            () => Store.TryInitializeExistingStore("secret", _storePath));
    }

    [Fact]
    public void TryInitializeExistingStore_WithCorrectPassword_ReturnsStore()
    {
        var created = new Store("secret", _storePath);
        created.AddProperty("a", "b");

        var reopened = Store.TryInitializeExistingStore("secret", _storePath);

        Assert.Equal("b", reopened.GetValue("a"));
    }

    [Fact]
    public void StoreFile_HasNoUtf8Bom()
    {
        var store = new Store("secret", _storePath);
        store.AddProperty("github", "token123");

        var bytes = File.ReadAllBytes(_storePath);

        Assert.False(StartsWithBom(bytes));
    }

    private static bool StartsWithBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
