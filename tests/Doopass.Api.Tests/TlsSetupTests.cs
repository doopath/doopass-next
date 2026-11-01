using System.Security.Cryptography.X509Certificates;
using Doopass.Cli;
using Doopass.Core;
using Doopass.Core.Configuration;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>The setup-tls command must leave a usable certificate and settings behind.</summary>
public sealed class TlsSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "doopass-tls-tests", Guid.NewGuid().ToString("N"));

    public TlsSetupTests() => AppPaths.OverrideRoot(_root);

    public void Dispose()
    {
        AppPaths.ResetOverride();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void CreatesLoadablePfxAndStoresTlsSettings()
    {
        var pfx = Path.Combine(_root, "doopass.pfx");

        Assert.Equal(0, TlsSetup.Run(["--pfx", pfx, "--pass", "test-pass"]));
        Assert.True(File.Exists(pfx));

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(pfx, "test-pass");
        Assert.True(certificate.HasPrivateKey);

        var api = new Settings().GetSection("api");
        Assert.Equal(Path.GetFullPath(pfx), api["tls_pfx"]);
        Assert.Equal("test-pass", api["tls_pass"]);
    }

    [Fact]
    public void RefusesToOverwriteWithoutForce()
    {
        var pfx = Path.Combine(_root, "doopass.pfx");

        Assert.Equal(0, TlsSetup.Run(["--pfx", pfx, "--pass", "test-pass"]));
        Assert.Equal(1, TlsSetup.Run(["--pfx", pfx, "--pass", "test-pass"]));
        Assert.Equal(0, TlsSetup.Run(["--pfx", pfx, "--pass", "new-pass", "--force"]));
    }
}
