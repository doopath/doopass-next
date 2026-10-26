namespace Doopass.Cli.Services;

/// <summary>
/// Shared master-password throttle for every login path (REST API and the Blazor
/// page). Five failures inside a minute block further attempts until the oldest
/// one ages out; ten consecutive failures add a 30 s lockout. One instance per
/// host, so the counters are shared by all clients as long as they share an IP -
/// the ASP.NET per-IP limiter cannot see Blazor circuit logins at all.
/// </summary>
public sealed class AuthThrottle
{
    public const int MaxFailuresPerWindow = 5;
    public const int FailuresBeforeLockout = 10;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Lockout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _lockedUntil;

    /// <summary>Time source; replaced with a fake clock in tests.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>How long the caller must wait, or <c>null</c> when an attempt is allowed now.</summary>
    public TimeSpan? GetRetryAfter()
    {
        lock (_gate)
        {
            var now = Clock();

            if (_lockedUntil is { } until && until > now)
            {
                return until - now;
            }

            Prune_locked(now);

            return _failures.Count >= MaxFailuresPerWindow
                ? _failures.Peek() + Window - now
                : null;
        }
    }

    public void RegisterFailure()
    {
        lock (_gate)
        {
            var now = Clock();
            Prune_locked(now);

            _failures.Enqueue(now);
            _consecutiveFailures++;

            if (_consecutiveFailures >= FailuresBeforeLockout)
            {
                _lockedUntil = now + Lockout;

                // The lockout subsumes the window; after it expires the caller
                // starts with a clean slate again.
                _failures.Clear();
                _consecutiveFailures = 0;
            }
        }
    }

    public void RegisterSuccess()
    {
        lock (_gate)
        {
            ResetCounters_locked();
        }
    }

    /// <summary>Forgets every failure (test isolation and an explicit admin reset).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            ResetCounters_locked();
        }
    }

    private void ResetCounters_locked()
    {
        _failures.Clear();
        _consecutiveFailures = 0;
        _lockedUntil = null;
    }

    private void Prune_locked(DateTimeOffset now)
    {
        while (_failures.Count > 0 && _failures.Peek() <= now - Window)
        {
            _failures.Dequeue();
        }
    }
}
