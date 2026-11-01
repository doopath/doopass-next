using Doopass.Core;

namespace Doopass.WebUi.Abstractions;

/// <summary>
/// Maps a host REST error envelope back to the Core exception the shared Razor
/// pages already understand, so a thin client behaves like the local session.
/// Shared by the mobile client (and unit-testable without a host) instead of
/// living in the Android project, which cannot be referenced from the test
/// projects.
/// </summary>
public static class VaultResponseErrors
{
    /// <summary>
    /// 409 covers three different vault states. The host's fixed messages are the
    /// only discriminator: a busy store and an uninitialized store are not
    /// "this pair already exists", and the pages render them differently.
    /// </summary>
    public static Exception Conflict(string message)
    {
        if (message.Contains("busy", StringComparison.OrdinalIgnoreCase))
        {
            return new StoreBusyError(message);
        }

        // The host uses both spellings: the login path says "not initialized",
        // the store operations say "isn't initialized".
        if (message.Contains("not initialized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("isn't initialized", StringComparison.OrdinalIgnoreCase))
        {
            return new StoreIsNotInitializedError(message);
        }

        return new PropertyAlreadyExistError(message);
    }
}
