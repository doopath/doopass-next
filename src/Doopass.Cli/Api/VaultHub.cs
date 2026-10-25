using Microsoft.AspNetCore.SignalR;

namespace Doopass.Cli.Api;

/// <summary>
/// Push channel for API clients. The token travels with the standard
/// <c>access_token</c> query parameter (SignalR's <c>WithAccessTokenProvider</c>
/// convention); on connect the token is validated and each connection joins its
/// own token group. Events are camelCase: <c>vaultChanged</c>, <c>backupsChanged</c>,
/// <c>settingsChanged</c>, <c>vaultLocked</c>.
/// </summary>
public sealed class VaultHub(ApiVaultCoordinator coordinator) : Hub
{
    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var token = http?.Request.Query["access_token"].ToString();

        if (string.IsNullOrEmpty(token) || !coordinator.IsTokenValid(token))
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, Group(token));
    }

    /// <summary>Group name carrying a vault token; the leading prefix avoids id collisions.</summary>
    public static string Group(string token) => $"doopass:{token}";
}

/// <summary>Relays server-side vault changes to connected hub clients.</summary>
public sealed class VaultHubRelay(IHubContext<VaultHub> hub, IVaultChangeNotifier notifier) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        notifier.VaultChanged += () => Fire(stoppingToken, "vaultChanged");
        notifier.BackupsChanged += () => Fire(stoppingToken, "backupsChanged");
        notifier.SettingsChanged += () => Fire(stoppingToken, "settingsChanged");
        notifier.VaultLocked += token => FireVaultLocked(stoppingToken, token);
        return Task.CompletedTask;
    }

    private void Fire(CancellationToken ct, string method, string? argument = null)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        _ = argument is null
            ? hub.Clients.All.SendAsync(method, CancellationToken.None)
            : hub.Clients.All.SendAsync(method, argument, CancellationToken.None);
    }

    private void FireVaultLocked(CancellationToken ct, string token)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        // An empty token means "no sessions remain": tell everyone.
        _ = string.IsNullOrEmpty(token)
            ? hub.Clients.All.SendAsync("vaultLocked", CancellationToken.None)
            : hub.Clients.Group(VaultHub.Group(token)).SendAsync("vaultLocked", CancellationToken.None);
    }
}
