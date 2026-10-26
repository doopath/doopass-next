namespace Doopass.Core;

/// <summary>Raised when the store file has not been initialized yet.</summary>
public sealed class StoreIsNotInitializedError : Exception
{
    public StoreIsNotInitializedError(string message) : base(message) { }
}

/// <summary>Raised when a requested property (key) does not exist in the store.</summary>
public sealed class PropertyDoesNotExistError : Exception
{
    public PropertyDoesNotExistError(string message) : base(message) { }
}

/// <summary>Raised when trying to add a property that already exists.</summary>
public sealed class PropertyAlreadyExistError : Exception
{
    public PropertyAlreadyExistError(string message) : base(message) { }
}

/// <summary>Raised when the store cannot be decrypted with the supplied password.</summary>
public sealed class IncorrectPasswordError : Exception
{
    public IncorrectPasswordError(string message) : base(message) { }

    public IncorrectPasswordError(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when a setting may not be changed through the UI/API (paths that decide
/// where data is written are read-only by policy).
/// </summary>
public sealed class ReadOnlySettingError : Exception
{
    public ReadOnlySettingError(string message) : base(message) { }
}

/// <summary>Raised when a setting value fails its format validation.</summary>
public sealed class InvalidSettingValueError : Exception
{
    public InvalidSettingValueError(string message) : base(message) { }
}

/// <summary>Raised when the store file is locked by another writer for longer than the lock timeout.</summary>
public sealed class StoreBusyError : Exception
{
    public StoreBusyError(string message) : base(message) { }
}

/// <summary>
/// Raised when the store file is not a valid Doopass payload: not base64, too
/// short to hold a token, or declaring an absurd key-derivation cost.
/// </summary>
public sealed class StoreCorruptedError : Exception
{
    public StoreCorruptedError(string message) : base(message) { }
}
