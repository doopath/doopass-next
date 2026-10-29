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

    /// <summary>Creates the descriptor over the device's saved connection state.</summary>
    /// <param name="connections">Persisted host, pin and token.</param>
    public HostConnectionState(ConnectionStore connections)
    {
        _connections = connections;
    }

    /// <inheritdoc />
    public string? Address => _connections.Host;

    /// <inheritdoc />
    public bool IsUnreachable => _unreachable;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public Task EnsureLoadedAsync() => _connections.LoadAsync();

    /// <inheritdoc />
    public void MarkReachable() => SetUnreachable(false);

    /// <inheritdoc />
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
