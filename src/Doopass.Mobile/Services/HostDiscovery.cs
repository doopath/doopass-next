using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Doopass.Core.Diagnostics;

namespace Doopass.Mobile.Services;

/// <summary>A host found on the LAN (or typed in by hand on the connect screen).</summary>
public sealed record HostCandidate(string Name, string Url, bool Tls, string? Fingerprint, string Version);

/// <summary>
/// Finds Doopass hosts in the LAN with a UDP broadcast (matching the host's
/// discovery service): sends <c>DOOPASS_DISCOVER</c> to port 7241 and collects
/// replies for ~1.5 s, deriving the host address from each reply's sender.
/// Requires a multicast lock on Wi-Fi so broadcast responses are not filtered.
/// </summary>
public sealed class HostDiscovery
{
    public const int AnnouncePort = 7241;

    private const string Probe = "DOOPASS_DISCOVER";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed record Reply(string? Service, string? Name, int Port, bool Tls, string? Fingerprint, string? Version);

    public async Task<IReadOnlyList<HostCandidate>> DiscoverAsync(TimeSpan? window = null, CancellationToken ct = default)
    {
        var found = new List<HostCandidate>();

        var multicastLock = AcquireMulticastLock();

        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var probe = Encoding.ASCII.GetBytes(Probe);
            await udp.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, AnnouncePort));

            var deadline = DateTime.UtcNow + (window ?? TimeSpan.FromMilliseconds(1500));

            while (DateTime.UtcNow < deadline)
            {
                Task<UdpReceiveResult> receive;

                try
                {
                    receive = udp.ReceiveAsync();
                }
                catch (SocketException)
                {
                    continue;
                }

                var finished = await Task.WhenAny(receive, Task.Delay(TimeSpan.FromMilliseconds(120)));

                if (finished != receive)
                {
                    continue;
                }

                var result = await receive;
                var remote = result.RemoteEndPoint;
                var received = Encoding.UTF8.GetString(result.Buffer);

                try
                {
                    var reply = JsonSerializer.Deserialize<Reply>(received, JsonOptions);

                    if (reply is not { Service: "doopass" })
                    {
                        continue;
                    }

                    var scheme = reply.Tls ? "https" : "http";
                    var url = $"{scheme}://{remote!.Address}:{reply.Port}";
                    var candidate = new HostCandidate(reply.Name ?? "Doopass host", url, reply.Tls, reply.Fingerprint, reply.Version ?? "5.0.0");

                    if (found.All(item => item.Url != candidate.Url))
                    {
                        found.Add(candidate);
                    }
                }
                catch (JsonException)
                {
                    // Not a Doopass reply; keep probing until the window closes.
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Host discovery failed", ex);
        }
        finally
        {
            multicastLock?.Dispose();
        }

        return found;
    }

    private static IDisposable? AcquireMulticastLock()
    {
        try
        {
#if ANDROID
            var wifi = (Android.Net.Wifi.WifiManager?)
                Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService);

            if (wifi is null)
            {
                return null;
            }

            var wifiLock = wifi.CreateMulticastLock("doopass-discovery");
            wifiLock.SetReferenceCounted(false);
            wifiLock.Acquire();
            return new MulticastLockHandle(wifiLock);
#else
            return null;
#endif
        }
        catch (Exception ex)
        {
            AppLogger.Error("Multicast lock acquisition failed", ex);
            return null;
        }
    }
}

/// <summary>MulticastLock itself is not IDisposable; release it on dispose.</summary>
#if ANDROID
file sealed class MulticastLockHandle(Android.Net.Wifi.WifiManager.MulticastLock wifiLock) : IDisposable
{
    public void Dispose()
    {
        try
        {
            if (wifiLock.IsHeld)
            {
                wifiLock.Release();
            }
        }
        catch
        {
            // Releasing twice is harmless.
        }
    }
}
#endif
