using Doopass.Cli.Api;
using Doopass.Core.Configuration;
using Doopass.Core.Storage;
using Doopass.Core.Validation;
using Doopass.WebUi.Abstractions;

namespace Doopass.Cli.Services;

/// <summary>
/// Per-circuit session holding the currently unlocked store. The store only
/// lives in server memory while the user is logged in. Every mutation raises
/// <see cref="IVaultChangeNotifier"/> events, which fan out to SignalR hub
/// clients (API tokens) and back into this circuit's <see cref="VaultEventRelay"/>
/// so open pages refresh.
/// </summary>
public sealed class DoopassSession : IVaultSession, IDisposable
{
    private readonly object _gate = new();
    private readonly Settings _settings;
    private readonly StoreBackup _backups;
    private readonly IVaultChangeNotifier? _notifier;
    private readonly VaultEventRelay? _relay;
    private Store? _store;

    /// <summary>
    /// Creates the session and wires the server-wide change events into this
    /// circuit's relay (when both are supplied by the host).
    /// </summary>
    /// <param name="settings">Host-wide settings.</param>
    /// <param name="notifier">Optional server-wide notifier.</param>
    /// <param name="relay">Optional per-circuit relay.</param>
    public DoopassSession(Settings settings, IVaultChangeNotifier? notifier = null, VaultEventRelay? relay = null)
    {
        _settings = settings;
        _notifier = notifier;
        _relay = relay;
        _backups = new StoreBackup(settings.StoreFile, settings.BackupsDir, settings.BackupExtension);

        // Server-side change fan-in: notifier (all clients) -> this circuit's relay.
        if (_notifier is not null && _relay is not null)
        {
            _notifier.VaultChanged += _relay.RaiseVaultChanged;
            _notifier.BackupsChanged += _relay.RaiseBackupsChanged;
            _notifier.SettingsChanged += _relay.RaiseSettingsChanged;
            _notifier.VaultLocked += OnVaultLocked;
        }
    }

    private void OnVaultLocked(string token) => _relay?.RaiseVaultLocked();

    /// <summary>Detaches this circuit from the server-wide notifier.</summary>
    public void Dispose()
    {
        if (_notifier is not null && _relay is not null)
        {
            _notifier.VaultChanged -= _relay.RaiseVaultChanged;
            _notifier.BackupsChanged -= _relay.RaiseBackupsChanged;
            _notifier.SettingsChanged -= _relay.RaiseSettingsChanged;
            _notifier.VaultLocked -= OnVaultLocked;
        }
    }

    /// <summary>The host-wide settings object (shared by every circuit).</summary>
    public Settings Settings => _settings;

    /// <summary>Backup helper bound to this vault.</summary>
    public StoreBackup Backups => _backups;

    /// <summary>True while this circuit holds a decrypted store.</summary>
    public bool IsUnlocked => _store is not null;

    /// <summary>True when the encrypted store file exists on disk.</summary>
    public bool StoreExists => Store.DoesStoreFileExist(_settings.StoreFile);

    /// <summary>Tries to unlock the store with the given password.</summary>
    /// <param name="password">The master password.</param>
    /// <returns>Success flag and a user-facing message.</returns>
    public (bool Success, string Message) Login(string password)
    {
        lock (_gate)
        {
            if (!StoreExists)
            {
                return (false, "Store is not initialized. Please initialize it first.");
            }

            var store = Store.TryInitializeStore(password, _settings.StoreFile);

            if (store is null)
            {
                return (false, "Incorrect password!");
            }

            _store = store;
            return (true, string.Empty);
        }
    }

    /// <summary>Creates a new store with the given password and unlocks it.</summary>
    /// <param name="password">The new master password.</param>
    /// <returns>Success flag and a user-facing message.</returns>
    public (bool Success, string Message) Init(string password)
    {
        lock (_gate)
        {
            if (StoreExists)
            {
                return (false, "Store already exists!");
            }

            var (valid, message) = new PasswordValidator(password).Validate();

            if (!valid)
            {
                return (false, message);
            }

            _store = new Store(password, _settings.StoreFile);
            return (true, "You successfully initialized a store!");
        }
    }

    /// <summary>Locks this circuit: the decrypted store and the password are dropped.</summary>
    public void Logout()
    {
        lock (_gate)
        {
            _store = null;
        }
    }

    /// <summary>Re-reads the store from disk (e.g. after restoring a backup).</summary>
    public void Reload()
    {
        lock (_gate)
        {
            if (_store is null)
            {
                return;
            }

            _store = new Store(_store.Password, _settings.StoreFile);
        }
    }

    /// <summary>Reads one secret value from the unlocked store.</summary>
    /// <param name="key">Key to read.</param>
    /// <returns>The stored value.</returns>
    public string GetValue(string key)
    {
        lock (_gate)
        {
            return _store!.GetValue(key);
        }
    }

    /// <summary>Lists every key of the unlocked store (empty when locked).</summary>
    /// <returns>Keys in storage order.</returns>
    public IReadOnlyList<string> GetKeys()
    {
        lock (_gate)
        {
            return _store?.GetKeys() ?? [];
        }
    }

    /// <summary>Adds a pair and notifies every client.</summary>
    /// <param name="key">New key; must be unique.</param>
    /// <param name="value">Secret value.</param>
    public void Add(string key, string value)
    {
        lock (_gate)
        {
            _store!.AddProperty(key, value);
        }

        _notifier?.NotifyVaultChanged();
    }

    /// <summary>Renames a key and replaces its value, then notifies every client.</summary>
    /// <param name="oldKey">Existing key.</param>
    /// <param name="newKey">Target key.</param>
    /// <param name="newValue">New value.</param>
    public void Update(string oldKey, string newKey, string newValue)
    {
        lock (_gate)
        {
            var store = _store!;
            store.RemoveProperty(oldKey);
            store.AddProperty(newKey, newValue);
        }

        _notifier?.NotifyVaultChanged();
    }

    /// <summary>Removes a key and notifies every client.</summary>
    /// <param name="key">Key to remove.</param>
    public void Remove(string key)
    {
        lock (_gate)
        {
            _store!.RemoveProperty(key);
        }

        _notifier?.NotifyVaultChanged();
    }

    // IVaultSession: awaitable facade over the synchronous storage calls, so the
    // same Razor components run against the local session or a remote API client.

    /// <inheritdoc />
    public ValueTask<VaultState> GetStateAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(new VaultState(StoreExists, IsUnlocked, Doopass.Core.Constants.Version));

    /// <inheritdoc />
    public ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default) =>
        ValueTask.FromResult(Login(password));

    /// <inheritdoc />
    public ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default) =>
        ValueTask.FromResult(Init(password));

    /// <inheritdoc />
    public ValueTask LogoutAsync(CancellationToken ct = default)
    {
        Logout();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ReloadAsync(CancellationToken ct = default)
    {
        Reload();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>(GetKeys());

    /// <inheritdoc />
    public ValueTask<string> GetValueAsync(string key, CancellationToken ct = default) =>
        ValueTask.FromResult(GetValue(key));

    /// <inheritdoc />
    public ValueTask AddAsync(string key, string value, CancellationToken ct = default)
    {
        Add(key, value);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default)
    {
        Update(oldKey, newKey, newValue);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        Remove(key);
        return ValueTask.CompletedTask;
    }

    /// <summary>Lists backup names without extension, newest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extensionless snapshot names.</returns>
    public ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>(_backups.GetBackupNames());

    /// <inheritdoc />
    public ValueTask CreateBackupAsync(CancellationToken ct = default)
    {
        _backups.MakeBackup();
        _notifier?.NotifyBackupsChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>Restores a backup by extensionless name; a safety backup is written first, then the session re-reads the store.</summary>
    /// <param name="backupName">Extensionless snapshot name.</param>
    /// <param name="ct">Cancellation token.</param>
    public ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default)
    {
        _backups.SwapStoreWithBackup(backupName + _settings.BackupExtension);
        Reload();
        _notifier?.NotifyBackupsChanged();
        _notifier?.NotifyVaultChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>Deletes a backup by extensionless name.</summary>
    /// <param name="backupName">Extensionless snapshot name.</param>
    /// <param name="ct">Cancellation token.</param>
    public ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default)
    {
        _backups.DeleteBackup(backupName + _settings.BackupExtension);
        _notifier?.NotifyBackupsChanged();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>>
        GetSettingsAsync(CancellationToken ct = default)
    {
        var sections = _settings.Sections
            .Select(section => (Section: section, Pairs: _settings.GetSection(section)))
            .ToList();
        return ValueTask.FromResult<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>>(sections);
    }

    /// <inheritdoc />
    public ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default)
    {
        _settings.ModifyProperty(section, key, value);
        _notifier?.NotifySettingsChanged();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default)
    {
        _settings.RestoreDefaults();
        _notifier?.NotifySettingsChanged();
        return ValueTask.CompletedTask;
    }
}
