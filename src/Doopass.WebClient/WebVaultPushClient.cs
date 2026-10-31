using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;

namespace Doopass.WebClient;

/// <summary>
/// SignalR push channel for the Blazor WebAssembly thin client: keeps open
/// pages fresh. The token comes from sessionStorage (per-tab); a lock event
/// clears it and raises VaultLocked so the guard navigates to login.
/// </summary>
public sealed class WebVaultPushClient : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly VaultEventRelay _relay;
    private HubConnection? _connection;
    private string? _connectionToken;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    public WebVaultPushClient(IJSRuntime js, VaultEventRelay relay)
    {
        _js = js;
        _relay = relay;
    }

    /// <summary>True when the live connection belongs to the current session token.</summary>
    public bool IsCurrentSession =>
        _connection?.State == HubConnectionState.Connected
        && string.Equals(_connectionToken, GetStoredTokenAsync().Result, StringComparison.Ordinal);

    public async Task StartAsync()
    {
        await _startGate.WaitAsync();

        try
        {
            await StopCoreAsync();

            var token = await GetStoredTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return;
            }

            // The WASM host is served from the same origin as the API/hub.
            _connection = new HubConnectionBuilder()
                .WithUrl("/hubs/vault", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) })
                .Build();

            _connectionToken = token;

            _connection.On("vaultChanged", () => _relay.RaiseVaultChanged());
            _connection.On("backupsChanged", () => _relay.RaiseBackupsChanged());
            _connection.On("settingsChanged", () => _relay.RaiseSettingsChanged());
            _connection.On("vaultLocked", () => OnVaultLocked(token));

            _connection.Closed += async exception =>
            {
                if (exception is not null)
                {
                    AppLogger.Error("Web vault push connection closed with an error", exception);
                }
            };

            try
            {
                await _connection.StartAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Web vault push connect failed", ex);
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async ValueTask<string?> GetStoredTokenAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("sessionStorage.getItem", "doopass_vault_token");
        }
        catch { return null; }
    }

    private void OnVaultLocked(string token)
    {
        // A late event from a token the user already left must not end the new session.
        var currentToken = GetStoredTokenAsync().Result;
        if (!string.Equals(currentToken, token, StringComparison.Ordinal))
        {
            return;
        }

        // Clear the token so the guard routes to login.
        _ = _js.InvokeVoidAsync("sessionStorage.removeItem", "doopass_vault_token");
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
            AppLogger.Error("Web vault push disconnect failed", ex);
        }

        _connection = null;
        _connectionToken = null;
    }

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
