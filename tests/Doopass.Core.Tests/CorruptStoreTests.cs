using System.Diagnostics;
using Doopass.Core.Crypto;
using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

/// <summary>
/// A damaged or tampered store file must produce a clear error, not an
/// out-of-range exception, and must never make the process chew CPU on a bogus
/// key-derivation cost.
/// </summary>
public sealed class CorruptStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _storePath;

    public CorruptStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "doopass-corrupt-tests", Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("AAAA")]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("not base64 at all!!")]
    public void ShortOrNonBase64Payload_IsCorrupted(string content)
    {
        File.WriteAllText(_storePath, content);

        Assert.Throws<StoreCorruptedError>(() => new Store("secret", _storePath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9_999)]
    [InlineData(int.MaxValue)]
    public void AbsurdIterationCount_IsRejectedQuickly(int iterations)
    {
        File.WriteAllText(_storePath, PayloadWithIterations(iterations));
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<StoreCorruptedError>(() => new Store("secret", _storePath));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void ValidFileWithWrongPassword_IsStillIncorrectPassword()
    {
        _ = new Store("secret", _storePath);

        Assert.Throws<IncorrectPasswordError>(() => new Store("wrong password", _storePath));
        Assert.Null(Store.TryInitializeStore("wrong password", _storePath));
    }

    [Fact]
    public void TryInitializeStore_DoesNotSwallowCorruption()
    {
        File.WriteAllText(_storePath, "AAAA");

        Assert.Throws<StoreCorruptedError>(() => Store.TryInitializeStore("secret", _storePath));
    }

    /// <summary>Builds a structurally valid payload (valid salt and a real token) with a patched cost.</summary>
    private static string PayloadWithIterations(int iterations)
    {
        var valid = Base64Url.Decode(Store.Encrypt("secret", []));
        var patched = (byte[])valid.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(patched.AsSpan(16, 4), iterations);
        return Base64Url.Encode(patched);
    }
}
