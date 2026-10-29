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
    /// <inheritdoc />
    public event Action? VaultChanged;

    /// <inheritdoc />
    public event Action? BackupsChanged;

    /// <inheritdoc />
    public event Action? SettingsChanged;

    /// <inheritdoc />
    public event Func<Task>? VaultLocked;

    /// <summary>Raises <see cref="VaultChanged"/> for every subscriber.</summary>
    public void RaiseVaultChanged() => VaultChanged?.Invoke();

    /// <summary>Raises <see cref="BackupsChanged"/> for every subscriber.</summary>
    public void RaiseBackupsChanged() => BackupsChanged?.Invoke();

    /// <summary>Raises <see cref="SettingsChanged"/> for every subscriber.</summary>
    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    /// <summary>Raises <see cref="VaultLocked"/> without awaiting the handlers.</summary>
    public void RaiseVaultLocked()
    {
        var handler = VaultLocked;
        if (handler is not null)
        {
            _ = handler();
        }
    }
}
