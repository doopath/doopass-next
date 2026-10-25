namespace Doopass.WebUi.Abstractions;

/// <summary>
/// Per-scope vault event relay. Pages subscribe in <c>OnInitialized</c> and
/// unsubscribe in <c>Dispose</c> to refresh their state when the vault changes,
/// wherever the change came from: another client over the REST API (the web
/// host relays the server-side <c>IVaultChangeNotifier</c> into this service)
/// or the MAUI thin client's SignalR push connection.
/// </summary>
public interface IVaultEvents
{
    /// <summary>Any vault content change: pairs added, updated or removed, and store reloads.</summary>
    event Action? VaultChanged;

    /// <summary>The backup list changed: created, restored or deleted.</summary>
    event Action? BackupsChanged;

    /// <summary>Application settings changed or were restored to defaults.</summary>
    event Action? SettingsChanged;

    /// <summary>The vault relocked: the caller's session ended (logout, TTL or host event).</summary>
    event Func<Task>? VaultLocked;
}

/// <summary>Scoped relay implementation; a host wires its event source into it (public for cross-assembly wiring).</summary>
public sealed class VaultEventRelay : IVaultEvents
{
    public event Action? VaultChanged;
    public event Action? BackupsChanged;
    public event Action? SettingsChanged;
    public event Func<Task>? VaultLocked;

    public void RaiseVaultChanged() => VaultChanged?.Invoke();

    public void RaiseBackupsChanged() => BackupsChanged?.Invoke();

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    public void RaiseVaultLocked()
    {
        var handler = VaultLocked;
        if (handler is not null)
        {
            _ = handler();
        }
    }
}
