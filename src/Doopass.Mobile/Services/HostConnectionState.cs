using Doopass.WebUi.Abstractions;

namespace Doopass.Mobile.Services;

/// <summary>
/// Exposes the host saved by <see cref="ConnectionStore"/> to the shared UI and
/// remembers whether the last call reached it. Registered only by the thin
/// client, so the web login screen has no host row and never shows the offline
/// banner.
/// </summary>
public sealed class HostConnectionState : IHostConnection
{
    private readonly ConnectionStore _connections;
    private volatile bool _unreachable;

    public HostConnectionState(ConnectionStore connections)
    {
        _connections = connections;
    }

    public string? Address => _connections.Host;

    public bool IsUnreachable => _unreachable;

    public event Action? Changed;

    public Task EnsureLoadedAsync() => _connections.LoadAsync();

    public void MarkReachable() => SetUnreachable(false);

    public void MarkUnreachable() => SetUnreachable(true);

    private void SetUnreachable(bool unreachable)
    {
        if (_unreachable == unreachable)
        {
            return;
        }

        _unreachable = unreachable;

        // Remote calls finish on thread-pool threads; the UI subscribes with a
        // handler that marshals back to its dispatcher.
        Changed?.Invoke();
    }
}
