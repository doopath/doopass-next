namespace Doopass.Core;

/// <summary>Raised when the store file has not been initialized yet.</summary>
public sealed class StoreIsNotInitializedError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public StoreIsNotInitializedError(string message) : base(message) { }
}

/// <summary>Raised when a requested property (key) does not exist in the store.</summary>
public sealed class PropertyDoesNotExistError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public PropertyDoesNotExistError(string message) : base(message) { }
}

/// <summary>Raised when trying to add a property that already exists.</summary>
public sealed class PropertyAlreadyExistError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public PropertyAlreadyExistError(string message) : base(message) { }
}

/// <summary>Raised when the store cannot be decrypted with the supplied password.</summary>
public sealed class IncorrectPasswordError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public IncorrectPasswordError(string message) : base(message) { }

    /// <summary>Creates the error and keeps the underlying cryptographic failure.</summary>
    /// <param name="message">Message shown to the caller.</param>
    /// <param name="innerException">The original exception (format or padding error).</param>
    public IncorrectPasswordError(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when a setting may not be changed through the UI/API (paths that decide
/// where data is written are read-only by policy).
/// </summary>
public sealed class ReadOnlySettingError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public ReadOnlySettingError(string message) : base(message) { }
}

/// <summary>Raised when a setting value fails its format validation.</summary>
public sealed class InvalidSettingValueError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public InvalidSettingValueError(string message) : base(message) { }
}

/// <summary>Raised when the store file is locked by another writer for longer than the lock timeout.</summary>
public sealed class StoreBusyError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public StoreBusyError(string message) : base(message) { }
}

/// <summary>
/// Raised when the store file is not a valid Doopass payload: not base64, too
/// short to hold a token, or declaring an absurd key-derivation cost.
/// </summary>
public sealed class StoreCorruptedError : Exception
{
    /// <summary>Creates the error with a user-facing message.</summary>
    /// <param name="message">Message shown to the caller.</param>
    public StoreCorruptedError(string message) : base(message) { }
}
