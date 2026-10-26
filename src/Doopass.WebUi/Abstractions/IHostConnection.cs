namespace Doopass.WebUi.Abstractions;

/// <summary>
/// Optional description of the remote host a thin client is attached to.
/// The web host owns its vault and never registers this service, so shared
/// pages check for it through <see cref="IServiceProvider"/> and show the host
/// actions only where another machine actually holds the vault.
/// </summary>
public interface IHostConnection
{
    /// <summary>Saved base URL of the host, e.g. <c>http://192.168.1.10:7240</c>.</summary>
    string? Address { get; }

    /// <summary>
    /// Loads the saved configuration before a page reads <see cref="Address"/>.
    /// The login screen may render before the route guard has loaded it.
    /// </summary>
    Task EnsureLoadedAsync();

    /// <summary>
    /// True when the last request to <see cref="Address"/> did not reach the
    /// host. A saved address goes stale when the host's IP changes, so the
    /// login screen needs to know before the user types a password.
    /// </summary>
    bool IsUnreachable { get; }

    /// <summary>Raised whenever <see cref="IsUnreachable"/> changes.</summary>
    event Action? Changed;

    /// <summary>Records that the host answered.</summary>
    void MarkReachable();

    /// <summary>Records that the host did not answer.</summary>
    void MarkUnreachable();
}
