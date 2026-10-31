using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doopass.Core;
using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;

namespace Doopass.WebClient;

/// <summary>
/// IVaultSession for the Blazor WebAssembly thin client: every call is a direct
/// HTTP request against the host REST API. HTTP statuses map back to Core
/// exceptions so the shared RCL pages behave identically across hosts.
/// Token lives in memory + sessionStorage (per-tab, survives reload, never cross-tab).
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

    // --- Token persistence ------------------------------------------------

    private void SetLocalToken(string? token) => _token = token;

    private async ValueTask PersistTokenAsync(string? token)
    {
        SetLocalToken(token);
        try
        {
            if (token is not null)
                await _js.InvokeVoidAsync("sessionStorage.setItem", "doopass_vault_token", token);
            else
                await _js.InvokeVoidAsync("sessionStorage.removeItem", "doopass_vault_token");
        }
        catch { /* sessionStorage unavailable in some edge cases */ }

        if (token is null)
            _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private async ValueTask RestoreTokenAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("sessionStorage.getItem", "doopass_vault_token");
            if (!string.IsNullOrEmpty(stored))
            {
                SetLocalToken(stored);
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", stored);
            }
        }
        catch { /* sessionStorage unavailable */ }
    }

    // --- Vault state / auth -----------------------------------------------

    public async ValueTask<VaultState> GetStateAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/api/vault/state",
                HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct, authOnly: false);
            return await ReadJsonAsync<StateDto>(response.Content, response.StatusCode, ct)
                is StateDto dto
                    ? new VaultState(dto.StoreExists, dto.Unlocked, dto.ServerVersion ?? "5.0.0")
                    : throw new InvalidOperationException("Empty state response.");
        }
        catch (HostUnreachableException) when (ct.IsCancellationRequested is false)
        {
            return new VaultState(false, false, "5.0.0 (offline)") { IsOffline = true };
        }
        catch (HttpRequestException) when (ct.IsCancellationRequested is false)
        {
            return new VaultState(false, false, "5.0.0 (offline)") { IsOffline = true };
        }
    }

    public async ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { password }, Json), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("/api/auth/login", content, ct);

        if (response.IsSuccessStatusCode)
        {
            var dto = await ReadJsonAsync<TokenDto>(response.Content, response.StatusCode, ct);
            await PersistTokenAsync(dto?.Token);
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
    }

    public async ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { password }, Json), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("/api/auth/init", content, ct);

        if (response.IsSuccessStatusCode)
        {
            var dto = await ReadJsonAsync<TokenDto>(response.Content, response.StatusCode, ct);
            await PersistTokenAsync(dto?.Token);
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
    }

    public async ValueTask LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("/api/auth/logout", null, ct);
        }
        catch (Exception)
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

    public async ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<KeysDto>("/api/vault/keys", ct);
        return dto!.Keys;
    }

    public async ValueTask<string> GetValueAsync(string key, CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<ValueDto>($"/api/vault/value?key={Uri.EscapeDataString(key)}", ct);
        return dto!.Value;
    }

    public async ValueTask AddAsync(string key, string value, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/vault/pairs", new { key, value }, ct);

    public async ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, "/api/vault/pairs", new { oldKey, newKey, newValue }, ct);

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/api/vault/pairs?key={Uri.EscapeDataString(key)}", null, ct);

    // --- Backups ----------------------------------------------------------

    public async ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<NamesDto>("/api/backups", ct);
        return dto!.Names;
    }

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

    private async ValueTask<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
            return await ReadJsonAsync<T>(response.Content, response.StatusCode, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new HostUnreachableException($"Host is unreachable: {ex.Message}", ex);
        }
    }

    private async ValueTask SendAsync(HttpMethod method, string url, object? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);

        if (content is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(content, Json), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async ValueTask EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct, bool authOnly = false)
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
                // Session gone (logout elsewhere, TTL): clear token and raise lock.
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
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }
}
