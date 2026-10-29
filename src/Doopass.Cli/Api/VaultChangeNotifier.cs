namespace Doopass.Cli.Api;

/// <summary>
/// Server-side change events raised by sessions mutating the vault. The SignalR
/// hub relay subscribes to push <c>vaultChanged</c> / <c>backupsChanged</c> /
/// <c>settingsChanged</c> / <c>vaultLocked</c> to API clients, and the Blazor
/// scopes re-read their state from the same events, so changes made through the
/// web UI, the REST API or another client become visible everywhere.
/// </summary>
public interface IVaultChangeNotifier
{
    /// <summary>Raised after pairs were added, updated or removed, or the store was reloaded.</summary>
    event Action? VaultChanged;

    /// <summary>Raised after a snapshot was created, restored or deleted.</summary>
    event Action? BackupsChanged;

    /// <summary>Raised after settings were edited or restored to defaults.</summary>
    event Action? SettingsChanged;

    /// <summary>
    /// Raised when a session ends or the vault relocks. The argument is the
    /// affected token, or an empty string when no sessions remain (broadcast).
    /// </summary>
    event Action<string>? VaultLocked;

    /// <summary>Notifies listeners that vault content changed.</summary>
    void NotifyVaultChanged();

    /// <summary>Notifies listeners that the backup list changed.</summary>
    void NotifyBackupsChanged();

    /// <summary>Notifies listeners that settings changed.</summary>
    void NotifySettingsChanged();

    /// <summary>Notifies listeners that a token ended or the vault relocked.</summary>
    /// <param name="token">Affected token, or an empty string to notify everyone.</param>
    void NotifyVaultLocked(string token);
}

/// <summary>Default server-wide notifier; subscribers are wired once at startup.</summary>
public sealed class VaultChangeNotifier : IVaultChangeNotifier
{
    /// <inheritdoc />
    public event Action? VaultChanged;

    /// <inheritdoc />
    public event Action? BackupsChanged;

    /// <inheritdoc />
    public event Action? SettingsChanged;

    /// <inheritdoc />
    public event Action<string>? VaultLocked;

    /// <inheritdoc />
    public void NotifyVaultChanged() => VaultChanged?.Invoke();

    /// <inheritdoc />
    public void NotifyBackupsChanged() => BackupsChanged?.Invoke();

    /// <inheritdoc />
    public void NotifySettingsChanged() => SettingsChanged?.Invoke();

    /// <inheritdoc />
    public void NotifyVaultLocked(string token) => VaultLocked?.Invoke(token);
}
