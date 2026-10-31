using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doopass.Core;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;

namespace Doopass.WebClient;

/// <summary>
/// IVaultSession for the Blazor WebAssembly thin client: every call is a direct
/// HTTP request against the host REST API. HTTP statuses map back to Core
/// exceptions so the shared RCL pages behave identically across hosts. Token
/// lives only in memory plus sessionStorage (per-tab, survives reload, never
/// cross-tab), and the bearer header is attached to each outgoing request.
/// </summary>
public sealed class WebApiVaultSession : IVaultSession, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _js;
    private readonly VaultEventRelay _relay;
    private string? _token;
    private bool _disposed;

    public WebApiVaultSession(HttpClient httpClient, IJSRuntime js, VaultEventRelay relay)
    {
        _httpClient = httpClient;
        _js = js;
        _relay = relay;
    }

    /// <summary>Raised after the local session is created (login or init), so hosts can open per-session resources such as the push channel.</summary>
    public event Func<Task>? SessionActivated;

    /// <summary>Raised after the local session ended (logout or a server 401/lock), so hosts can stop per-session resources.</summary>
    public event Func<Task>? SessionCleared;

    // --- Token persistence ------------------------------------------------

    private AuthenticationHeaderValue? BearerHeader() =>
        string.IsNullOrEmpty(_token) ? null : new AuthenticationHeaderValue("Bearer", _token);

    private void SetLocalToken(string? token) => _token = token;

    /// <summary>
    /// Stores the token in memory and sessionStorage, and ends the local session
    /// when clearing. Raising <see cref="SessionCleared"/> lets the host stop the
    /// SignalR push connection that belongs to the now-absent token.
    /// </summary>
    private async ValueTask PersistTokenAsync(string? token)
    {
        SetLocalToken(token);

        try
        {
            if (token is not null)
                await _js.InvokeVoidAsync("sessionStorage.setItem", "doopass_vault_token", token);
            else
            {
                await _js.InvokeVoidAsync("sessionStorage.removeItem", "doopass_vault_token");
                await RaiseSessionClearedAsync();
            }
        }
        catch
        {
            // sessionStorage unavailable in some edge cases; the in-memory token still drives the next request.
        }

        if (token is not null)
        {
            await RaiseSessionActivatedAsync();
        }
    }

    private async Task RaiseSessionClearedAsync()
    {
        try
        {
            await (SessionCleared?.Invoke() ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Stopping the session after a clear failed", ex);
        }
    }

    private async Task RaiseSessionActivatedAsync()
    {
        try
        {
            await (SessionActivated?.Invoke() ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Starting the session after a login failed", ex);
        }
    }

    /// <summary>
    /// Restores the local session from sessionStorage before the first render,
    /// so an unlocked tab survives a reload (or opens in a fresh one) without a
    /// new login. The bearer is then attached to each outgoing request.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("sessionStorage.getItem", "doopass_vault_token");
            if (!string.IsNullOrEmpty(stored))
            {
                SetLocalToken(stored);
            }
        }
        catch
        {
            // sessionStorage unavailable; start locked.
        }
    }

    /// <summary>Reads the token back from sessionStorage once, without touching local state.</summary>
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

    // --- Vault state / auth -----------------------------------------------

    private static VaultState OfflineState() =>
        new(false, false, $"{Constants.Version} (offline)") { IsOffline = true };

    public async ValueTask<VaultState> GetStateAsync(CancellationToken ct = default)
    {
        using var request = BuildRequest(HttpMethod.Get, "/api/vault/state");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException) when (ct.IsCancellationRequested is false)
        {
            return OfflineState();
        }

        await EnsureSuccessAsync(response, ct);
        var dto = await ReadJsonAsync<StateDto>(response.Content, response.StatusCode, ct);
        return dto is StateDto parsed
            ? new VaultState(parsed.StoreExists, parsed.Unlocked, parsed.ServerVersion ?? Constants.Version)
            : throw new InvalidOperationException("Empty state response.");
    }

    public async ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await PostAuthJsonAsync(new { password }, ct);
        }
        catch (HostUnreachableException) when (ct.IsCancellationRequested is false)
        {
            return (false, "You can't reach the Doopass host. Make sure it's running and try again.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var dto = await ReadJsonAsync<TokenDto>(response.Content, response.StatusCode, ct);
                await PersistTokenAsync(dto?.Token);
                return (true, string.Empty);
            }

            return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
        }
    }

    public async ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await PostAuthJsonAsync(new { password }, ct);
        }
        catch (HostUnreachableException) when (ct.IsCancellationRequested is false)
        {
            return (false, "You can't reach the Doopass host. Make sure it's running and try again.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var dto = await ReadJsonAsync<TokenDto>(response.Content, response.StatusCode, ct);
                await PersistTokenAsync(dto?.Token);
                return (true, string.Empty);
            }

            return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
        }
    }

    public async ValueTask LogoutAsync(CancellationToken ct = default)
    {
        using var request = BuildRequest(HttpMethod.Post, "/api/auth/logout");

        try
        {
            // Send with the current bearer so the host can revoke this server session.
            await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch
        {
            // Host gone — local session still ends.
        }
        finally
        {
            await PersistTokenAsync(null);
        }
    }

    public ValueTask ReloadAsync(CancellationToken ct = default) =>
        ValueTask.CompletedTask; // the remote session always reflects the host

    // --- Vault pairs ------------------------------------------------------

    public async ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default) =>
        (await GetJsonAsync<KeysDto>("/api/vault/keys", ct))!.Keys;

    public async ValueTask<string> GetValueAsync(string key, CancellationToken ct = default) =>
        (await GetJsonAsync<ValueDto>($"/api/vault/value?key={Uri.EscapeDataString(key)}", ct))!.Value;

    public async ValueTask AddAsync(string key, string value, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/vault/pairs", new { key, value }, ct);

    public async ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, "/api/vault/pairs", new { oldKey, newKey, newValue }, ct);

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/api/vault/pairs?key={Uri.EscapeDataString(key)}", null, ct);

    // --- Backups ----------------------------------------------------------

    public async ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default) =>
        (await GetJsonAsync<NamesDto>("/api/backups", ct))!.Names;

    public async ValueTask CreateBackupAsync(CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/backups", null, ct);

    public async ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, $"/api/backups/restore?name={Uri.EscapeDataString(backupName)}", null, ct);

    public async ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/api/backups?name={Uri.EscapeDataString(backupName)}", null, ct);

    // --- Settings ---------------------------------------------------------

    public async ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<SettingsDto>("/api/settings", ct);
        return dto!.Sections
            .Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Pairs))
            .ToList();
    }

    public async ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, $"/api/settings/{Uri.EscapeDataString(section)}/{Uri.EscapeDataString(key)}", new { value }, ct);

    public async ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/settings/restore-defaults", null, ct);

    // --- Request/response plumbing ----------------------------------------

    /// <summary>Builds a request for the current bearer token. No token means no Authorization header.</summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        var auth = BearerHeader();
        if (auth is not null)
        {
            request.Headers.Authorization = auth;
        }

        return request;
    }

    /// <summary>Sends a login/init body; transport failures surface as <see cref="HostUnreachableException"/> so the login screen shows the host banner.</summary>
    private async ValueTask<HttpResponseMessage> PostAuthJsonAsync(object payload, CancellationToken ct)
    {
        using var request = BuildRequest(HttpMethod.Post, "/api/auth/login", new StringContent(
            JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ct.IsCancellationRequested is false)
        {
            throw new HostUnreachableException($"Host is unreachable: {ex.Message}", ex);
        }

        return response;
    }

    private async ValueTask<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var request = BuildRequest(HttpMethod.Get, url);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ct.IsCancellationRequested is false)
        {
            throw new HostUnreachableException($"Host is unreachable: {ex.Message}", ex);
        }

        // Server errors are raised by EnsureSuccessAsync and must stay as plain
        // HttpRequestException (not HostUnreachable), so this wrapper only covers
        // genuine transport failures, not 5xx responses.
        await EnsureSuccessAsync(response, ct);
        return await ReadJsonAsync<T>(response.Content, response.StatusCode, ct);
    }

    private async ValueTask SendAsync(HttpMethod method, string url, object? content, CancellationToken ct)
    {
        var payload = content is null ? null : new StringContent(
            JsonSerializer.Serialize(content, Json), Encoding.UTF8, "application/json");

        using var request = BuildRequest(method, url, payload);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ct.IsCancellationRequested is false)
        {
            throw new HostUnreachableException($"Host is unreachable: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, ct);
    }

    private async ValueTask EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var message = await ReadErrorMessageAsync(response.Content, response.StatusCode, ct);

        switch (status)
        {
            case 401:
                // Session gone (logout elsewhere, TTL): clear the token and raise lock.
                await PersistTokenAsync(null);
                _relay.RaiseVaultLocked();
                throw new UnauthorizedAccessException(message);
            case 409:
                throw new PropertyAlreadyExistError(message);
            case 404:
                throw new PropertyDoesNotExistError(message);
            case 429:
                throw new InvalidOperationException(message);
            default:
                // 5xx (and anything else): a server problem, not a dead host.
                throw new HttpRequestException($"{status} {(HttpStatusCode)status}: {message}");
        }
    }

    private async ValueTask<T?> ReadJsonAsync<T>(HttpContent content, HttpStatusCode status, CancellationToken ct)
    {
        var text = await content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException)
        {
            throw new HttpRequestException($"{(int)status} {(HttpStatusCode)status}: unexpected response payload");
        }
    }

    private async ValueTask<string> ReadErrorMessageAsync(HttpContent content, HttpStatusCode status, CancellationToken ct)
    {
        try
        {
            var text = await content.ReadAsStringAsync(ct);
            var error = JsonSerializer.Deserialize<ErrorDto>(text, Json)?.Error;
            return string.IsNullOrEmpty(error) ? $"{(int)status} {status}" : error;
        }
        catch (JsonException)
        {
            return $"{(int)status} {status}";
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
        _httpClient.Dispose();
    }
}
