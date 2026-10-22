using Doopass.Core.Configuration;
using Doopass.Core.Storage;
using Doopass.Core.Validation;

namespace Doopass.Tui;

/// <summary>Result of a login attempt.</summary>
public enum LoginStatus
{
    Success,
    IncorrectPassword,
    NotInitialized,
}

/// <summary>Result of an init-store attempt.</summary>
public enum InitStatus
{
    Success,
    AlreadyExists,
    InvalidPassword,
}

/// <summary>
/// Shared state for the TUI: settings, the currently unlocked store and
/// backup helper.
/// </summary>
internal sealed class DoopassContext
{
    public DoopassContext(Settings settings)
    {
        Settings = settings;
        Backups = new StoreBackup(settings.StoreFile, settings.BackupsDir, settings.BackupExtension);
    }

    public Settings Settings { get; }

    public StoreBackup Backups { get; }

    /// <summary>The unlocked store, or <c>null</c> when logged out.</summary>
    public Store? CurrentStore { get; private set; }

    public bool StoreExists => Store.DoesStoreFileExist(Settings.StoreFile);

    public LoginStatus Login(string password)
    {
        if (!StoreExists)
        {
            return LoginStatus.NotInitialized;
        }

        var store = Store.TryInitializeStore(password, Settings.StoreFile);

        if (store is null)
        {
            return LoginStatus.IncorrectPassword;
        }

        CurrentStore = store;
        return LoginStatus.Success;
    }

    public InitStatus Init(string password, out string message)
    {
        if (StoreExists)
        {
            message = "Store already exists!";
            return InitStatus.AlreadyExists;
        }

        var (valid, validationMessage) = new PasswordValidator(password).Validate();

        if (!valid)
        {
            message = validationMessage;
            return InitStatus.InvalidPassword;
        }

        CurrentStore = new Store(password, Settings.StoreFile);
        message = "You successfully initialized a store!";
        return InitStatus.Success;
    }

    public void Logout() => CurrentStore = null;

    /// <summary>Re-reads the store from disk (e.g. after restoring a backup).</summary>
    public void ReloadStore()
    {
        if (CurrentStore is not null)
        {
            CurrentStore = new Store(CurrentStore.Password, Settings.StoreFile);
        }
    }
}
