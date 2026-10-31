using Doopass.WebUi.Abstractions;
using Xunit;

namespace Doopass.WebClient.Tests;

public sealed class WebVaultPushClientTests
{
    [Fact]
    public async Task StopAsync_then_StartAsync_without_token_does_not_throw()
    {
        var push = new WebVaultPushClient(new FakeJsRuntime(), new VaultEventRelay(), "http://localhost:7240");

        await push.StopAsync();
        await push.StartAsync(); // no token in storage: just returns, no network
        await push.StopAsync();

        await push.DisposeAsync();
    }
}
