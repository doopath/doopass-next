using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Doopass.Core.Diagnostics;

namespace Doopass.Cli.Api;

/// <summary>What the host announces on the LAN so phones can find it without typing an address.</summary>
public sealed record DiscoveryAnnounce(string Name, int Port, bool Tls, string? Fingerprint, string Version)
{
    /// <summary>Whether the host should answer discovery probes at all.</summary>
    [JsonIgnore]
    public bool Enabled { get; init; }
}

/// <summary>
/// Listens for UDP broadcast probes (<c>DOOPASS_DISCOVER</c>, port 7241) and
/// answers with the host announcement so thin clients can find the vault in the
/// LAN. Started as a hosted service after the web server itself is up. A bind
/// failure is logged and disables announcing instead of failing the host.
/// </summary>
public sealed class DiscoveryService : IHostedService, IDisposable
{
    /// <summary>The exact ASCII datagram a client must send to be answered.</summary>
    public const string ProbeText = "DOOPASS_DISCOVER";

    /// <summary><see cref="ProbeText"/> as bytes, for byte-exact comparison.</summary>
    public static readonly byte[] Probe = "DOOPASS_DISCOVER"u8.ToArray();

    private readonly DiscoveryAnnounce _announce;
    private readonly Socket? _socket;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Creates the announcer; binds nothing until <see cref="StartAsync"/> runs.</summary>
    /// <param name="announce">Payload to send back to probes, plus the enable flag.</param>
    public DiscoveryService(DiscoveryAnnounce announce)
    {
        _announce = announce;

        if (!announce.Enabled)
        {
            return;
        }

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }
        catch (SocketException ex)
        {
            AppLogger.Error("UDP discovery disabled: cannot create the announce socket", ex);
        }
        catch (System.IO.IOException ex)
        {
            AppLogger.Error("UDP discovery disabled: cannot create the announce socket", ex);
        }
    }

    /// <summary>Binds UDP 7241 and starts answering probes; a bind failure disables announcing.</summary>
    /// <param name="cancellationToken">Startup cancellation token (unused).</param>
    /// <returns>A completed task; listening runs in the background.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_socket is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            _socket.Bind(new IPEndPoint(IPAddress.Any, 7241));
        }
        catch (SocketException ex)
        {
            AppLogger.Error("UDP discovery disabled: port 7241 could not be bound", ex);
            Dispose();
            return Task.CompletedTask;
        }

        _listenTask = ListenAsync();
        AppLogger.Info("UDP discovery announcing on port 7241");
        return Task.CompletedTask;
    }

    /// <summary>Stops listening and closes the socket.</summary>
    /// <param name="cancellationToken">Shutdown cancellation token (unused).</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _socket?.Close();

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown races are fine.
        }

        return Task.CompletedTask;
    }

    /// <summary>JSON body of the announce reply, exposed for discovery-format tests.</summary>
    public static string FormatProbe(DiscoveryAnnounce announce) =>
        JsonSerializer.Serialize(
            new
            {
                service = "doopass",
                name = announce.Name,
                port = announce.Port,
                tls = announce.Tls,
                fingerprint = announce.Fingerprint,
                version = announce.Version,
            }, JsonOptions);

    private async Task ListenAsync()
    {
        var buffer = new byte[Probe.Length];

        while (!_cts.IsCancellationRequested)
        {
            EndPoint remote = new IPEndPoint(IPAddress.None, 0);

            SocketReceiveFromResult received;

            try
            {
                received = await _socket!.ReceiveFromAsync(buffer, SocketFlags.None, remote, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.Interrupted or SocketError.OperationAborted)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (!buffer.AsSpan(0, received.ReceivedBytes).SequenceEqual(Probe))
            {
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(FormatProbe(_announce));

            try
            {
                await _socket.SendToAsync(bytes, SocketFlags.None, received.RemoteEndPoint, CancellationToken.None);
            }
            catch (SocketException)
            {
                // The prober is gone; nothing to do.
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Cancels the listener and releases the socket and cancellation source.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _socket?.Dispose();
    }
}
