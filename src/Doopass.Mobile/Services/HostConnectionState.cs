using Doopass.WebUi.Abstractions;

namespace Doopass.Mobile.Services;

/// <summary>
/// Exposes the host saved by <see cref="ConnectionStore"/> to the shared UI.
/// Registered only by the thin client, so the web login screen has no host row.
/// </summary>
public sealed class HostConnectionState : IHostConnection
{
    private readonly ConnectionStore _connections;

    public HostConnectionState(ConnectionStore connections)
    {
        _connections = connections;
    }

    public string? Address => _connections.Host;

    public Task EnsureLoadedAsync() => _connections.LoadAsync();
}
