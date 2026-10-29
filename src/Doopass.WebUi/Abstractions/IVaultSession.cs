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
    /// <summary>Reads the current vault state without revealing any secret.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Existence, unlock state and server version.</returns>
    ValueTask<VaultState> GetStateAsync(CancellationToken ct = default);

    /// <summary>Tries to unlock the existing store with the master password.</summary>
    /// <param name="password">The master password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether the unlock succeeded and a user-facing message.</returns>
    ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default);

    /// <summary>Creates a new store and leaves it unlocked.</summary>
    /// <param name="password">The new master password (validated first).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether the store was created and a user-facing message.</returns>
    ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default);

    /// <summary>Locks this session: the decrypted vault and the password are dropped.</summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask LogoutAsync(CancellationToken ct = default);

    /// <summary>Re-reads the store from disk under the current password.</summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ReloadAsync(CancellationToken ct = default);

    /// <summary>Lists every key in the vault.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All keys, in storage order.</returns>
    ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default);

    /// <summary>Reads a single secret value.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored value.</returns>
    ValueTask<string> GetValueAsync(string key, CancellationToken ct = default);

    /// <summary>Adds a new key/value pair.</summary>
    /// <param name="key">Unique key; duplicates are rejected.</param>
    /// <param name="value">Secret value; may be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask AddAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Renames a key and/or replaces its value (remove + add).</summary>
    /// <param name="oldKey">Existing key.</param>
    /// <param name="newKey">Target key.</param>
    /// <param name="newValue">New value.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default);

    /// <summary>Removes a key from the vault.</summary>
    /// <param name="key">Key to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Lists available snapshots.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Names of all backups, newest first, without their extension.</returns>
    ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default);

    /// <summary>Creates a dated snapshot of the current vault.</summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask CreateBackupAsync(CancellationToken ct = default);

    /// <summary>Restores a snapshot; the host writes a safety backup first and re-reads the store.</summary>
    /// <param name="backupName">Extensionless snapshot name.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default);

    /// <summary>Deletes a snapshot.</summary>
    /// <param name="backupName">Extensionless snapshot name.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default);

    /// <summary>Reads every settings section with its key/value pairs.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Section name plus a copy of its pairs.</returns>
    ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Writes a single setting value.</summary>
    /// <param name="section">Existing section name (for example <c>local</c>).</param>
    /// <param name="key">Setting key inside that section.</param>
    /// <param name="value">New value (always a string).</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default);

    /// <summary>Rewrites <c>settings.json</c> from the embedded defaults.</summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default);
}
