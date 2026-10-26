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
    event Action? VaultChanged;
    event Action? BackupsChanged;
    event Action? SettingsChanged;
    /// <summary>The vault really locked: no session is left, so the store was dropped.</summary>
    event Action<string>? VaultLocked;

    /// <summary>One token expired (or was dropped) while other sessions may still hold the vault.</summary>
    event Action<string>? SessionExpired;

    void NotifyVaultChanged();
    void NotifyBackupsChanged();
    void NotifySettingsChanged();
    void NotifyVaultLocked(string token);
    void NotifySessionExpired(string token);
}

public sealed class VaultChangeNotifier : IVaultChangeNotifier
{
    public event Action? VaultChanged;
    public event Action? BackupsChanged;
    public event Action? SettingsChanged;
    public event Action<string>? VaultLocked;
    public event Action<string>? SessionExpired;

    public void NotifyVaultChanged() => VaultChanged?.Invoke();

    public void NotifyBackupsChanged() => BackupsChanged?.Invoke();

    public void NotifySettingsChanged() => SettingsChanged?.Invoke();

    public void NotifyVaultLocked(string token) => VaultLocked?.Invoke(token);

    public void NotifySessionExpired(string token) => SessionExpired?.Invoke(token);
}
