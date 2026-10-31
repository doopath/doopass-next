using System.Net.Http.Headers;
using System.Text.Json;
using Doopass.Cli.Api;
using Doopass.Core;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;

namespace Doopass.Cli.Services;

/// <summary>
/// IVaultSession that talks to the local REST API (/api/*) of the same process.
/// HTTP calls are delegated to the browser via JS interop (BrowserHttpClient),
/// so requests appear in the browser's Network tab. Token is persisted in
/// localStorage for cross-tab sync.
/// </summary>
internal sealed class LocalApiService : IVaultSession, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BrowserHttpClient _http;
    private readonly ApiVaultCoordinator? _coordinator;
    private string? _currentToken;
    private bool _disposed;

    /// <summary>
    /// Creates a service that uses the browser HTTP client. The coordinator
    /// is optional — when present, its ActiveToken is used as a fallback
    /// (for the server-side circuit); otherwise localStorage is the sole source.
    /// </summary>
    public LocalApiService(BrowserHttpClient browserHttp, ApiVaultCoordinator? coordinator = null)
    {
        _http = browserHttp;
        _coordinator = coordinator;
    }

    /// <summary>
    /// Factory constructor for tests: creates a BrowserHttpClient that wraps
    /// an HttpClient instead of using JS interop.
    /// </summary>
    public LocalApiService(IHttpClientFactory factory, int port, ApiVaultCoordinator coordinator)
        : this(new BrowserHttpClient(factory, port, coordinator), coordinator)
    {
    }

    /// <summary>
    /// Returns the current session token: from the local override first,
    /// then from the server coordinator (if present), then from localStorage.
    /// </summary>
    private async ValueTask<string?> GetTokenAsync()
    {
        if (_currentToken is not null) return _currentToken;
        if (_coordinator?.ActiveToken is { } serverToken) return serverToken;
        return await _http.GetTokenAsync();
    }

    /// <summary>Synchronises the token to localStorage so other tabs and the
    /// SignalR client can read it.</summary>
    private async Task SyncTokenAsync(string? token)
    {
        _currentToken = token;
        await _http.SetTokenAsync(token);
    }

    // --- Vault state / auth -----------------------------------------------

    public async ValueTask<VaultState> GetStateAsync(CancellationToken ct = default)
    {
        var res = await SendGetAsync("/api/vault/state", ct);
        var dto = await ReadJsonAsync<StateDto>(res.Body, res.Status, ct);

        if (dto is null)
        {
            throw new InvalidOperationException("The host returned an empty state response.");
        }

        return new VaultState(dto.StoreExists, dto.Unlocked, dto.ServerVersion ?? Doopass.Core.Constants.Version);
    }

    public async ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default)
    {
        var res = await SendPostAsync("/api/auth/login", new { password }, ct);

        if (res.Ok)
        {
            var dto = await ReadJsonAsync<TokenDto>(res.Body, res.Status, ct);
            await SyncTokenAsync(dto?.Token);
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(res.Body, res.Status, ct));
    }

    public async ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default)
    {
        var res = await SendPostAsync("/api/auth/init", new { password }, ct);

        if (res.Ok)
        {
            var dto = await ReadJsonAsync<TokenDto>(res.Body, res.Status, ct);
            await SyncTokenAsync(dto?.Token);
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(res.Body, res.Status, ct));
    }

    public async ValueTask LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await SendPostAsync("/api/auth/logout", null, ct);
        }
        catch (Exception)
        {
            // Host gone — local session still ends.
        }
        finally
        {
            await SyncTokenAsync(null);
        }
    }

    // --- Vault pairs ------------------------------------------------------

    public async ValueTask ReloadAsync(CancellationToken ct = default)
    {
        await SendPostAsync("/api/vault/reload", null, ct);
    }

    public async ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default)
    {
        var res = await SendGetAsync("/api/vault/keys", ct);
        var dto = await ReadJsonAsync<KeysDto>(res.Body, res.Status, ct);
        return dto!.Keys;
    }

    public async ValueTask<string> GetValueAsync(string key, CancellationToken ct = default)
    {
        var res = await SendGetAsync($"/api/vault/value?key={Uri.EscapeDataString(key)}", ct);
        var dto = await ReadJsonAsync<ValueDto>(res.Body, res.Status, ct);
        return dto!.Value;
    }

    public async ValueTask AddAsync(string key, string value, CancellationToken ct = default)
    {
        await SendPostAsync("/api/vault/pairs", new { key, value }, ct);
    }

    public async ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default)
    {
        await SendPutAsync("/api/vault/pairs", new { oldKey, newKey, newValue }, ct);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        await SendDeleteAsync($"/api/vault/pairs?key={Uri.EscapeDataString(key)}", ct);
    }

    // --- Backups ----------------------------------------------------------

    public async ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default)
    {
        var res = await SendGetAsync("/api/backups", ct);
        var dto = await ReadJsonAsync<NamesDto>(res.Body, res.Status, ct);
        return dto!.Names;
    }

    public async ValueTask CreateBackupAsync(CancellationToken ct = default)
    {
        await SendPostAsync("/api/backups", null, ct);
    }

    public async ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default)
    {
        await SendPostAsync($"/api/backups/restore?name={Uri.EscapeDataString(backupName)}", null, ct);
    }

    public async ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default)
    {
        await SendDeleteAsync($"/api/backups?name={Uri.EscapeDataString(backupName)}", ct);
    }

    // --- Settings ---------------------------------------------------------

    public async ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default)
    {
        var res = await SendGetAsync("/api/settings", ct);
        var dto = await ReadJsonAsync<SettingsDto>(res.Body, res.Status, ct);
        return dto!.Sections
            .Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Pairs))
            .ToList();
    }

    public async ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default)
    {
        await SendPutAsync($"/api/settings/{Uri.EscapeDataString(section)}/{Uri.EscapeDataString(key)}", new { value }, ct);
    }

    public async ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default)
    {
        await SendPostAsync("/api/settings/restore-defaults", null, ct);
    }

    // --- Request/response plumbing ----------------------------------------

    private async Task<HttpResponse> SendGetAsync(string url, CancellationToken ct)
    {
        try
        {
            var res = await _http.GetAsync(url, ct);
            await EnsureSuccessAsync(res, ct);
            return res;
        }
        catch (JSDisconnectedException)
        {
            throw new HostUnreachableException("Host is unreachable (JS disconnected)", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not HostUnreachableException)
        {
            AppLogger.Error($"GET {url} failed", ex);
            throw;
        }
    }

    private async Task<HttpResponse> SendPostAsync(string url, object? body, CancellationToken ct)
    {
        try
        {
            var res = await _http.PostAsync(url, body, ct);
            await EnsureSuccessAsync(res, ct);
            return res;
        }
        catch (JSDisconnectedException)
        {
            throw new HostUnreachableException("Host is unreachable (JS disconnected)", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not HostUnreachableException)
        {
            AppLogger.Error($"POST {url} failed", ex);
            throw;
        }
    }

    private async Task<HttpResponse> SendPutAsync(string url, object? body, CancellationToken ct)
    {
        try
        {
            var res = await _http.PutAsync(url, body, ct);
            await EnsureSuccessAsync(res, ct);
            return res;
        }
        catch (JSDisconnectedException)
        {
            throw new HostUnreachableException("Host is unreachable (JS disconnected)", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not HostUnreachableException)
        {
            AppLogger.Error($"PUT {url} failed", ex);
            throw;
        }
    }

    private async Task<HttpResponse> SendDeleteAsync(string url, CancellationToken ct)
    {
        try
        {
            var res = await _http.DeleteAsync(url, ct);
            await EnsureSuccessAsync(res, ct);
            return res;
        }
        catch (JSDisconnectedException)
        {
            throw new HostUnreachableException("Host is unreachable (JS disconnected)", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not HostUnreachableException)
        {
            AppLogger.Error($"DELETE {url} failed", ex);
            throw;
        }
    }

    private async ValueTask EnsureSuccessAsync(HttpResponse res, CancellationToken ct)
    {
        if (res.Ok) return;

        var status = res.Status;
        var message = await ReadErrorMessageAsync(res.Body, status, ct);

        switch (status)
        {
            case 401:
                throw new UnauthorizedAccessException(message);
            case 409:
                throw new PropertyAlreadyExistError(message);
            case 404:
                throw new PropertyDoesNotExistError(message);
            case 429:
                throw new InvalidOperationException(message);
            default:
                throw new HttpRequestException($"{status} {(System.Net.HttpStatusCode)status}: {message}");
        }
    }

    private static async ValueTask<T?> ReadJsonAsync<T>(string text, int status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException ex)
        {
            AppLogger.Error($"JSON deserialization failed for status {status}", ex);
            throw new HttpRequestException($"{status} unexpected response payload");
        }
    }

    private static async ValueTask<string> ReadErrorMessageAsync(string text, int status, CancellationToken ct)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorDto>(text, Json)?.Error;
            return string.IsNullOrEmpty(error) ? $"{status} {status}" : error;
        }
        catch (JsonException)
        {
            return $"{status} {status}";
        }
    }

    // --- DTOs -------------------------------------------------------------

    private sealed record StateDto(bool StoreExists, bool Unlocked, string? ServerVersion);
    private sealed record TokenDto(string Token, string? ServerVersion);
    private sealed record KeysDto(IReadOnlyList<string> Keys);
    private sealed record ValueDto(string Value);
    private sealed record NamesDto(IReadOnlyList<string> Names);
    private sealed record SettingsSectionDto(string Name, IReadOnlyDictionary<string, string> Pairs);
    private sealed record SettingsDto(IReadOnlyList<SettingsSectionDto> Sections);
    private sealed record ErrorDto(string? Error);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
