using System.Text.Json;
using Doopass.Cli.Api;
using Xunit;

namespace Doopass.Api.Tests;

public sealed class DiscoveryPayloadTests
{
    [Fact]
    public void Probe_reply_carries_the_documented_fields()
    {
        var reply = DiscoveryService.FormatProbe(new DiscoveryAnnounce("home-pc", 7240, true, "ab12", "5.0.0"));

        using var document = JsonDocument.Parse(reply);
        var root = document.RootElement;

        Assert.Equal("doopass", root.GetProperty("service").GetString());
        Assert.Equal("home-pc", root.GetProperty("name").GetString());
        Assert.Equal(7240, root.GetProperty("port").GetInt32());
        Assert.True(root.GetProperty("tls").GetBoolean());
        Assert.Equal("ab12", root.GetProperty("fingerprint").GetString());
        Assert.Equal("5.0.0", root.GetProperty("version").GetString());
    }

    [Fact]
    public void Probe_reply_reports_null_fingerprint_without_tls()
    {
        var reply = DiscoveryService.FormatProbe(new DiscoveryAnnounce("home-pc", 7240, false, null, "5.0.0"));

        using var document = JsonDocument.Parse(reply);
        var root = document.RootElement;

        Assert.False(root.GetProperty("tls").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fingerprint").ValueKind);
    }

    [Fact]
    public void Probe_constant_is_the_documented_ascii_magic()
    {
        Assert.Equal("DOOPASS_DISCOVER", DiscoveryService.ProbeText);
    }
}
