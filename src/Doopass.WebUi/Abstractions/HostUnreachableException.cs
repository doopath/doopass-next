namespace Doopass.WebUi.Abstractions;

/// <summary>
/// Thrown by thin-client sessions when a request never reached the host:
/// connection refused, DNS failure, or a timeout on a stale address. Pages use
/// it to offer the host picker instead of reporting a bad password.
/// </summary>
public sealed class HostUnreachableException : Exception
{
    public HostUnreachableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
