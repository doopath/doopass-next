namespace Doopass.WebUi.Abstractions;

/// <summary>
/// What the client knows about the host vault: whether the store file exists,
/// whether some session has unlocked it, and the host version. Used by the
/// login / initialization screens and by thin clients probing a host.
/// </summary>
public sealed record VaultState(bool StoreExists, bool IsUnlocked, string ServerVersion)
{
    /// <summary>
    /// True only on thin clients that could not reach their host; the other
    /// values then say nothing about the vault. Always false on the web host.
    /// </summary>
    public bool IsOffline { get; init; }
}

/// <summary>
/// The vault operations the UI components need. Both hosts implement it:
/// the web server wraps its per-circuit <c>DoopassSession</c> (synchronous core
/// storage calls wrapped in tasks), and the MAUI thin client talks to the REST
/// API of a remote host. Backup names are always extensionless — the host
/// builds the real file names itself. Methods are awaitable because the
/// remote implementation performs HTTP round-trips.
/// </summary>
public interface IVaultSession
{
    ValueTask<VaultState> GetStateAsync(CancellationToken ct = default);
    ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default);
    ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default);
    ValueTask LogoutAsync(CancellationToken ct = default);
    ValueTask ReloadAsync(CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default);
    ValueTask<string> GetValueAsync(string key, CancellationToken ct = default);
    ValueTask AddAsync(string key, string value, CancellationToken ct = default);
    ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default);
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <returns>Names of all backups, newest first, without their extension.</returns>
    ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default);
    ValueTask CreateBackupAsync(CancellationToken ct = default);
    ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default);
    ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default);

    ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default);
    ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default);
    ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default);
}
