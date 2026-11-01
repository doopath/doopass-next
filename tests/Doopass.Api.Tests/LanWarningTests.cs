using Doopass.Cli.Api;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// <c>--lan</c> without TLS is still allowed (scripts and the README depend on
/// it) but it must not be silent: everything travels in cleartext.
/// </summary>
public sealed class LanWarningTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task Lan_without_tls_warns_on_stderr()
    {
        _ = fixture; // only needed so the class fixture redirects DOOPASS_HOME
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);

        try
        {
            var app = WebHostFactory.BuildWebHost(new WebHostOptions
            {
                Port = 0,
                Host = "127.0.0.1",
                Lan = true,
                OpenBrowser = false,
            });

            await app.DisposeAsync();
        }
        finally
        {
            Console.SetError(original);
        }

        var stderr = captured.ToString();
        Assert.Contains("plaintext HTTP", stderr);
        Assert.Contains("unencrypted", stderr);
    }
}
