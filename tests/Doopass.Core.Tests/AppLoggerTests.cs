using Doopass.Core.Diagnostics;
using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

public sealed class AppLoggerTests : IDisposable
{
    private readonly string _dir;

    public AppLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "doopass-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
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
    public void StoreOperations_AreLogged_WithoutSecrets()
    {
        var logFile = Path.Combine(_dir, "doopass.log");
        var storePath = Path.Combine(_dir, "store.enc");

        AppLogger.Initialize(logFile);

        var store = new Store("secret", storePath);
        store.AddProperty("github", "token123");
        store.SetValue("github", "totally-secret-value");
        store.RemoveProperty("github");

        var text = File.ReadAllText(logFile);

        Assert.Contains("Store initialized", text);
        Assert.Contains("Property added: github", text);
        Assert.Contains("Property updated: github", text);
        Assert.Contains("Property removed: github", text);

        // Values must never end up in the log.
        Assert.DoesNotContain("token123", text);
        Assert.DoesNotContain("totally-secret-value", text);
    }
}
