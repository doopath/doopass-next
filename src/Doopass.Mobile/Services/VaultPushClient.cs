using Microsoft.AspNetCore.SignalR.Client;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;
using Doopass.WebUi.Services;

namespace Doopass.Mobile.Services;

/// <summary>
/// SignalR push channel to the host hub: keeps open pages fresh. The token
/// rides the standard access_token provider; a lock clears the stored token
/// and raises the relay's VaultLocked so the guard navigates back to the
/// connect / login flow.
/// </summary>
public sealed class VaultPushClient : IAsyncDisposable
{
    private readonly ConnectionStore _connections;
    private readonly VaultEventRelay _relay;
    private HubConnection? _connection;
    private string? _connectionHost;
    private string? _connectionToken;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    /// <summary>Creates the push client over the device's saved host and token.</summary>
    /// <param name="connections">Persisted connection state.</param>
    /// <param name="relay">Per-app event relay the hub events are forwarded to.</param>
    public VaultPushClient(ConnectionStore connections, VaultEventRelay relay)
    {
        _connections = connections;
        _relay = relay;
    }

    /// <summary>
    /// True when the live connection belongs to the saved host and token. After
    /// the host is changed, the old connection is still open for a moment and
    /// must not be mistaken for the channel of the new session.
    /// </summary>
    public bool IsCurrentSession =>
        _connection?.State == HubConnectionState.Connected
        && string.Equals(_connectionHost, _connections.Host, StringComparison.Ordinal)
        && string.Equals(_connectionToken, _connections.Token, StringComparison.Ordinal);

    /// <summary>Starts (or restarts) the hub connection for the current session.</summary>
    public async Task StartAsync()
    {
        await _startGate.WaitAsync();

        try
        {
            await StopCoreAsync();

            if (!_connections.IsConfigured || string.IsNullOrEmpty(_connections.Token))
            {
                return;
            }

            var host = _connections.Host!;
            var url = $"{host.TrimEnd('/')}/hubs/vault";
            var token = _connections.Token;

            _connection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) })
                .Build();

            _connectionHost = host;
            _connectionToken = token;

            _connection.On("vaultChanged", () => _relay.RaiseVaultChanged());
            _connection.On("backupsChanged", () => _relay.RaiseBackupsChanged());
            _connection.On("settingsChanged", () => _relay.RaiseSettingsChanged());
            _connection.On("vaultLocked", () => OnVaultLocked(host, token));

            _connection.Closed += async exception =>
            {
                if (exception is not null)
                {
                    AppLogger.Error("Vault push connection closed with an error", exception);
                }

                // A dropped connection without a token is nothing to report.
            };

            try
            {
                await _connection.StartAsync();
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Error("Vault push connect failed (host unreachable?)", ex);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Vault push connect failed", ex);
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    private void OnVaultLocked(string host, string token)
    {
        // A late event from a host/token the user already left (after switching
        // hosts or logging out) must not end the new session.
        if (!string.Equals(_connections.Host, host, StringComparison.Ordinal)
            || !string.Equals(_connections.Token, token, StringComparison.Ordinal))
        {
            return;
        }

        // The token is already invalidated server-side; forget it and let the
        // guard route the user through the connect / login screens again.
        _ = _connections.SetTokenAsync(null);
        _relay.RaiseVaultLocked();
    }

    private async Task StopCoreAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await _connection.StopAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Vault push disconnect failed", ex);
        }

        _connection = null;
        _connectionHost = null;
        _connectionToken = null;
    }

    /// <summary>Stops the hub connection and releases the start gate.</summary>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        await _startGate.WaitAsync();

        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _startGate.Release();
            _startGate.Dispose();
        }
    }
}
