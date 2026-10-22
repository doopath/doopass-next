namespace Doopass.Core.Validation;

/// <summary>Validates the master password using the original Doopass rules.</summary>
public sealed class PasswordValidator
{
    public PasswordValidator(string password) => Password = password;

    public string Password { get; }

    /// <summary>
    /// Returns a tuple: whether the password is valid, and a human-readable message.
    /// </summary>
    public (bool Valid, string Message) Validate()
    {
        if (Password.Length < 1)
        {
            return (false, "Password is too small!");
        }

        if (Password[0] == ' ')
        {
            return (false, "Password should not start with space!");
        }

        if (Password[^1] == ' ')
        {
            return (false, "Password should not end with space!");
        }

        return (true, "Everything is fine!");
    }
}
