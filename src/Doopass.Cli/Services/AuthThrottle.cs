namespace Doopass.Cli.Services;

/// <summary>
/// Shared master-password throttle for every login path (REST API and the Blazor
/// page). One instance per host, so the failure counter is global rather than
/// per client: a LAN attacker who keeps guessing can no longer lock the owner
/// out. Instead of refusing attempts, the throttle inserts a growing pause
/// before the password check once five failures fell inside the sliding minute
/// (1 s, 2 s, 4 s, at most 5 s) and a successful login clears the count. The
/// owner can therefore always log in; raw REST request volume is still capped by
/// the ASP.NET per-IP limiter, which sees the network but not Blazor circuits.
/// </summary>
public sealed class AuthThrottle
{
    /// <summary>Number of failures inside the window after which attempts start to be delayed.</summary>
    public const int DelayStartsAfter = 5;

    /// <summary>Upper bound on the pause inserted before a login attempt.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _failures = new();

    /// <summary>Time source; replaced with a fake clock in tests.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// How long to pause before checking the password. Never refuses an attempt:
    /// with five failures in the sliding window the pause grows 1 s → 2 s → 4 s
    /// and stays at <see cref="MaxDelay"/> after that, so the owner always gets in.
    /// </summary>
    public TimeSpan DelayBeforeAttempt()
    {
        lock (_gate)
        {
            var now = Clock();
            Prune_locked(now);

            if (_failures.Count < DelayStartsAfter)
            {
                return TimeSpan.Zero;
            }

            // 5 failures -> 1 s, 6 -> 2 s, 7 -> 4 s, 8+ -> the 5 s ceiling.
            var doublings = Math.Min(_failures.Count - DelayStartsAfter, 3);
            var seconds = Math.Min(1L << doublings, (long)MaxDelay.TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// Performs the pause computed by <see cref="DelayBeforeAttempt"/>; replaced
    /// in tests so they record the delays instead of waiting for them.
    /// </summary>
    internal Func<TimeSpan, Task> DelayHook { get; set; } = static delay => Task.Delay(delay);

    public void RegisterFailure()
    {
        lock (_gate)
        {
            var now = Clock();
            Prune_locked(now);
            _failures.Enqueue(now);
        }
    }

    public void RegisterSuccess()
    {
        lock (_gate)
        {
            _failures.Clear();
        }
    }

    /// <summary>Forgets every failure (test isolation and an explicit admin reset).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _failures.Clear();
        }
    }

    private void Prune_locked(DateTimeOffset now)
    {
        while (_failures.Count > 0 && _failures.Peek() <= now - Window)
        {
            _failures.Dequeue();
        }
    }
}
