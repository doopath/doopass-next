using Doopass.Core.Configuration;
using Doopass.Core.Storage;
using Doopass.Core.Validation;

namespace Doopass.Cli.Services;

/// <summary>
/// Per-circuit session holding the currently unlocked store. The store only
/// lives in server memory while the user is logged in.
/// </summary>
public sealed class DoopassSession
{
    private readonly object _gate = new();
    private readonly Settings _settings;
    private readonly StoreBackup _backups;
    private Store? _store;

    public DoopassSession(Settings settings)
    {
        _settings = settings;
        _backups = new StoreBackup(settings.StoreFile, settings.BackupsDir, settings.BackupExtension);
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
    }

    public void Update(string oldKey, string newKey, string newValue)
    {
        lock (_gate)
        {
            var store = _store!;
            store.RemoveProperty(oldKey);
            store.AddProperty(newKey, newValue);
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _store!.RemoveProperty(key);
        }
    }
}
