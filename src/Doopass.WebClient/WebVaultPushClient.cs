using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;

namespace Doopass.WebClient;

/// <summary>
/// SignalR push channel for the Blazor WebAssembly thin client: keeps open
/// pages fresh. The token comes from sessionStorage (one tab, survives reload);
/// a lock event clears it and raises <c>VaultLocked</c> so the guard routes to
/// login. The connection is started explicitly after the session is ready and
/// stopped when the session ends.
/// </summary>
public sealed class WebVaultPushClient : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly VaultEventRelay _relay;
    private readonly string _hubBaseAddress;
    private HubConnection? _connection;
    private string? _connectionToken;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    /// <summary>Creates the push channel for the host that served the WASM app.</summary>
    /// <param name="js">Browser interop used to read the stored token.</param>
    /// <param name="relay">Per-app event relay shared with the UI.</param>
    /// <param name="hubBaseAddress">Origin of the host that serves the app.</param>
    public WebVaultPushClient(IJSRuntime js, VaultEventRelay relay, string hubBaseAddress)
    {
        _js = js;
        _relay = relay;
        _hubBaseAddress = hubBaseAddress;
    }

    /// <summary>Opens the SignalR hub for the token stored in sessionStorage.</summary>
    public async Task StartAsync()
    {
        await _startGate.WaitAsync();

        try
        {
            await StopCoreAsync();

            var token = await ReadStoredTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return;
            }

            // WASM UI is served from the same origin as the API/hub, so the hub lives
            // at <origin>/hubs/vault on any SPA route. HttpClient.BaseAddress cannot be
            // *read* in Blazor WebAssembly (the getter throws), so the host base address
            // is captured in the constructor and used here (no Uri(string,string) overload
            // that works reliably on the trimmed WASM BCL).
            var hubUri = _hubBaseAddress.TrimEnd('/') + "/hubs/vault";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUri, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) })
                .Build();

            _connectionToken = token;

            _connection.On("vaultChanged", () => _relay.RaiseVaultChanged());
            _connection.On("backupsChanged", () => _relay.RaiseBackupsChanged());
            _connection.On("settingsChanged", () => _relay.RaiseSettingsChanged());
            _connection.On("vaultLocked", () => OnVaultLocked(_connectionToken));

            _connection.Closed += exception =>
            {
                if (exception is not null)
                {
                    AppLogger.Error("Web vault push connection closed with an error", exception);
                }

                return Task.CompletedTask;
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

    private async ValueTask<string?> ReadStoredTokenAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("sessionStorage.getItem", "doopass_vault_token");
        }
        catch
        {
            return null;
        }
    }

    private void OnVaultLocked(string token)
    {
        // A late event from a token the user already left must not end the new session.
        _ = InvokeCurrentTokenAsync(async current =>
        {
            if (!string.Equals(current, token, StringComparison.Ordinal))
            {
                return;
            }

            // Clear the token so the guard routes to login.
            await _js.InvokeVoidAsync("sessionStorage.removeItem", "doopass_vault_token");
            _relay.RaiseVaultLocked();
        });
    }

    private async Task InvokeCurrentTokenAsync(Func<string?, Task> action)
    {
        try
        {
            var current = await _js.InvokeAsync<string?>("sessionStorage.getItem", "doopass_vault_token");
            await action(current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Reading the current vault token failed", ex);
        }
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

    /// <summary>
    /// Stops the channel when the session ends, but keeps the gate alive so a
    /// later login in the same tab can start it again.
    /// </summary>
    public async Task StopAsync()
    {
        await _startGate.WaitAsync();

        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <inheritdoc />
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
