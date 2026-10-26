using System.Net;
using System.Net.NetworkInformation;
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
/// The probe goes to both 255.255.255.255 and the directed broadcast of every
/// IPv4 interface, because some Wi-Fi stacks (and default routes through a VPN)
/// drop the global broadcast. Requires a multicast lock on Wi-Fi so broadcast
/// responses are not filtered.
/// </summary>
public sealed class HostDiscovery
{
    public const int AnnouncePort = 7241;

    private const string Probe = "DOOPASS_DISCOVER";

    private static readonly byte[] ProbeBytes = Encoding.ASCII.GetBytes(Probe);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed record Reply(string? Service, string? Name, int Port, bool Tls, string? Fingerprint, string? Version);

    public async Task<IReadOnlyList<HostCandidate>> DiscoverAsync(TimeSpan? window = null, CancellationToken ct = default)
    {
        var found = new List<HostCandidate>();
        var duration = window ?? TimeSpan.FromMilliseconds(1500);

        using var multicastLock = AcquireMulticastLock();

        try
        {
            // UdpClient.SendAsync to a broadcast address fails with
            // "Permission denied" (EACCES) unless SO_BROADCAST is enabled.
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var sent = 0;

            foreach (var target in BroadcastTargets())
            {
                try
                {
                    await udp.SendAsync(ProbeBytes.AsMemory(), target, ct);
                    sent++;
                }
                catch (SocketException ex)
                {
                    AppLogger.Error($"Discovery probe to {target} was rejected", ex);
                }
            }

            if (sent == 0)
            {
                AppLogger.Error("Discovery probe was not sent to any interface");
                return found;
            }

            // One outstanding receive at a time: UdpClient rejects a second
            // ReceiveAsync while the previous one is still pending, which is
            // what made the old poll-by-120 ms loop fail silently.
            using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            windowCts.CancelAfter(duration);

            while (!windowCts.IsCancellationRequested)
            {
                UdpReceiveResult result;

                try
                {
                    result = await udp.ReceiveAsync(windowCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    AppLogger.Error("Discovery receive failed", ex);
                    break;
                }

                TryAddCandidate(found, result);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Host discovery failed", ex);
        }

        return found;
    }

    /// <summary>
    /// Global broadcast plus the directed broadcast of each active IPv4
    /// interface (e.g. 192.168.1.255 for 192.168.1.42/24).
    /// </summary>
    private static IEnumerable<IPEndPoint> BroadcastTargets()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (seen.Add(IPAddress.Broadcast.ToString()))
        {
            yield return new IPEndPoint(IPAddress.Broadcast, AnnouncePort);
        }

        NetworkInterface[] interfaces;

        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Discovery could not enumerate the network interfaces", ex);
            yield break;
        }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
            {
                // Loopback, VPNs and virtual bridges are not the LAN the phone
                // is on; the global broadcast above still covers them at best.
                continue;
            }

            IPInterfaceProperties properties;

            try
            {
                properties = nic.GetIPProperties();
            }
            catch (Exception)
            {
                // A vanishing USB/VPN interface; the remaining ones still count.
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var mask = unicast.IPv4Mask;

                if (mask is null || mask.Equals(IPAddress.Any))
                {
                    continue;
                }

                var addressBytes = unicast.Address.GetAddressBytes();
                var maskBytes = mask.GetAddressBytes();

                if (addressBytes.Length != 4 || maskBytes.Length != 4)
                {
                    continue;
                }

                var broadcastBytes = new byte[4];
                var hasHostBits = false;

                for (var i = 0; i < 4; i++)
                {
                    broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);

                    if ((byte)~maskBytes[i] != 0)
                    {
                        hasHostBits = true;
                    }
                }

                if (!hasHostBits)
                {
                    // /32 (point-to-point tunnel): there is no broadcast address.
                    continue;
                }

                var endpoint = new IPEndPoint(new IPAddress(broadcastBytes), AnnouncePort);

                if (seen.Add(endpoint.ToString()))
                {
                    yield return endpoint;
                }
            }
        }
    }

    private static void TryAddCandidate(List<HostCandidate> found, UdpReceiveResult result)
    {
        try
        {
            var received = Encoding.UTF8.GetString(result.Buffer);
            var reply = JsonSerializer.Deserialize<Reply>(received, JsonOptions);

            if (reply is not { Service: "doopass" } || reply.Port is < 1 or > 65535)
            {
                return;
            }

            var remote = result.RemoteEndPoint.Address;

            if (remote.IsIPv4MappedToIPv6)
            {
                remote = remote.MapToIPv4();
            }

            var scheme = reply.Tls ? "https" : "http";
            var url = $"{scheme}://{remote}:{reply.Port}";
            var candidate = new HostCandidate(reply.Name ?? "Doopass host", url, reply.Tls, reply.Fingerprint, reply.Version ?? "5.0.0");

            if (found.All(item => !string.Equals(item.Url, candidate.Url, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(candidate);
            }
        }
        catch (JsonException)
        {
            // Not a Doopass reply; keep probing until the window closes.
        }
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

            var wifiLock = wifi.CreateMulticastLock("doopass-discovery")!;
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
