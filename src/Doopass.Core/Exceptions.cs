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
