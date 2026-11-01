namespace Doopass.Cli.Api;

/// <summary>
/// Drops sessions whose sliding or absolute TTL ran out, even when no client
/// makes another request. Without this the decrypted store would stay in host
/// memory indefinitely after the last browser tab or phone closed.
/// </summary>
public sealed class SessionJanitor(ApiVaultCoordinator coordinator) : BackgroundService
{
    /// <summary>How often expired tokens are swept (default 30 seconds).</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                coordinator.PruneExpiredSessions();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is stopping; nothing left to sweep.
        }
    }
}
