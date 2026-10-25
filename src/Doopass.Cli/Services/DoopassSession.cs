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

    public Settings Settings => _settings;

    public StoreBackup Backups => _backups;

    public bool IsUnlocked => _store is not null;

    public bool StoreExists => Store.DoesStoreFileExist(_settings.StoreFile);

    /// <summary>Tries to unlock the store with the given password.</summary>
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

    /// <summary>Creates a new store with the given password.</summary>
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

    public string GetValue(string key)
    {
        lock (_gate)
        {
            return _store!.GetValue(key);
        }
    }

    public IReadOnlyList<string> GetKeys()
    {
        lock (_gate)
        {
            return _store?.GetKeys() ?? [];
        }
    }

    public void Add(string key, string value)
    {
        lock (_gate)
        {
            _store!.AddProperty(key, value);
        }

        _notifier?.NotifyVaultChanged();
    }

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

    public ValueTask<VaultState> GetStateAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(new VaultState(StoreExists, IsUnlocked, Doopass.Core.Constants.Version));

    public ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default) =>
        ValueTask.FromResult(Login(password));

    public ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default) =>
        ValueTask.FromResult(Init(password));

    public ValueTask LogoutAsync(CancellationToken ct = default)
    {
        Logout();
        return ValueTask.CompletedTask;
    }

    public ValueTask ReloadAsync(CancellationToken ct = default)
    {
        Reload();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>(GetKeys());

    public ValueTask<string> GetValueAsync(string key, CancellationToken ct = default) =>
        ValueTask.FromResult(GetValue(key));

    public ValueTask AddAsync(string key, string value, CancellationToken ct = default)
    {
        Add(key, value);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default)
    {
        Update(oldKey, newKey, newValue);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        Remove(key);
        return ValueTask.CompletedTask;
    }

    /// <summary>Lists backup names without extension, newest first.</summary>
    public ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>(_backups.GetBackupNames());

    public ValueTask CreateBackupAsync(CancellationToken ct = default)
    {
        _backups.MakeBackup();
        _notifier?.NotifyBackupsChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>Restores a backup by extensionless name; a safety backup is written first, then the session re-reads the store.</summary>
    public ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default)
    {
        _backups.SwapStoreWithBackup(backupName + _settings.BackupExtension);
        Reload();
        _notifier?.NotifyBackupsChanged();
        _notifier?.NotifyVaultChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>Deletes a backup by extensionless name.</summary>
    public ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default)
    {
        _backups.DeleteBackup(backupName + _settings.BackupExtension);
        _notifier?.NotifyBackupsChanged();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>>
        GetSettingsAsync(CancellationToken ct = default)
    {
        var sections = _settings.Sections
            .Select(section => (Section: section, Pairs: _settings.GetSection(section)))
            .ToList();
        return ValueTask.FromResult<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>>(sections);
    }

    public ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default)
    {
        _settings.ModifyProperty(section, key, value);
        _notifier?.NotifySettingsChanged();
        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default)
    {
        _settings.RestoreDefaults();
        _notifier?.NotifySettingsChanged();
        return ValueTask.CompletedTask;
    }
}
