namespace Doopass.Core.Storage;

/// <summary>
/// A cross-process exclusive lock on the store file, implemented as a sidecar
/// <c>store.enc.lock</c> opened with <see cref="FileShare.None"/>. The lock file
/// is intentionally never deleted: on Windows an open file cannot be removed,
/// and on Unix deleting it races with another process that just opened it. An
/// empty sidecar next to <c>store.enc</c> is harmless.
/// </summary>
public sealed class StoreFileLock : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private const int RetryDelayMs = 20;

    private readonly FileStream _stream;

    private StoreFileLock(FileStream stream) => _stream = stream;

    /// <summary>Acquires the lock, waiting up to <paramref name="timeout"/> (5 s by default).</summary>
    public static StoreFileLock Acquire(string path, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            try
            {
                return new StoreFileLock(
                    new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                // Another process (or another Store instance) is writing; retry.
                Thread.Sleep(RetryDelayMs);
            }
            catch (IOException)
            {
                throw new StoreBusyError("The vault is busy, try again.");
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
