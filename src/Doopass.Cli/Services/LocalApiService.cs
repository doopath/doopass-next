using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Doopass.Cli.Api;
using Doopass.Core;
using Doopass.Core.Diagnostics;
using Doopass.WebUi.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Doopass.Cli.Services;

/// <summary>
/// IVaultSession that talks to the local REST API (/api/*) of the same process.
/// Every operation is an HTTP call with a Bearer token taken from the current
/// Blazor circuit's authentication identity, so /api endpoints see the same
/// session context as the originating request.
/// </summary>
internal sealed class LocalApiService : IVaultSession
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly object _gate = new();
    private HttpContext? _currentContext;
    private string? _token;

    public LocalApiService(IHttpClientFactory factory, int port, IHttpContextAccessor ctx)
    {
        _http = factory.CreateClient();
        _http.BaseAddress = new Uri($"http://localhost:{port}/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _currentContext = ctx.HttpContext;
    }

    private string? Token
    {
        get
        {
            lock (_gate)
            {
                if (_token is not null) return _token;
                var ctx = _currentContext ?? _staticCtx;
                _token = ctx?.Items[Api.VaultAuth.TokenItemKey] as string;
                return _token;
            }
        }
    }

    // Captured once for calls before DI wiring completes (e.g., during host build).
    private static HttpContext? _staticCtx;

    internal static void RegisterStaticAccessor(IHttpContextAccessor accessor)
    {
        if (_staticCtx is null) _staticCtx = accessor.HttpContext;
    }

    // --- Vault state / auth -----------------------------------------------

    public async ValueTask<VaultState> GetStateAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/api/vault/state", HttpCompletionOption.ResponseHeadersRead, ct);
        var dto = await ReadJsonAsync<StateDto>(response.Content, response.StatusCode, ct);

        if (dto is null)
        {
            throw new InvalidOperationException("The host returned an empty state response.");
        }

        return new VaultState(dto.StoreExists, dto.Unlocked, dto.ServerVersion ?? Doopass.Core.Constants.Version);
    }

    public async ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default)
    {
        using var response = await PostAuthAsync("/api/auth/login", new { password }, ct);

        if (response.IsSuccessStatusCode)
        {
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
    }

    public async ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default)
    {
        using var response = await PostAuthAsync("/api/auth/init", new { password }, ct);

        if (response.IsSuccessStatusCode)
        {
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
    }

    public async ValueTask LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await SendJsonAsync(HttpMethod.Post, "/api/auth/logout", null, ct);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("no connection"))
        {
            // Host gone — local session still ends.
        }
    }

    // --- Vault pairs ------------------------------------------------------

    public ValueTask ReloadAsync(CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Post, "/api/vault/reload", null, ct);

    public async ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default)
    {
        var dto = await GetDtoAsync<KeysDto>("/api/vault/keys", ct);
        return dto!.Keys;
    }

    public async ValueTask<string> GetValueAsync(string key, CancellationToken ct = default)
    {
        var dto = await GetDtoAsync<ValueDto>($"/api/vault/value?key={Uri.EscapeDataString(key)}", ct);
        return dto!.Value;
    }

    public ValueTask AddAsync(string key, string value, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Post, "/api/vault/pairs", new { key, value }, ct);

    public ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Put, "/api/vault/pairs", new { oldKey, newKey, newValue }, ct);

    public ValueTask RemoveAsync(string key, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Delete, $"/api/vault/pairs?key={Uri.EscapeDataString(key)}", null, ct);

    // --- Backups ----------------------------------------------------------

    public async ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default)
    {
        var dto = await GetDtoAsync<NamesDto>("/api/backups", ct);
        return dto!.Names;
    }

    public ValueTask CreateBackupAsync(CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Post, "/api/backups", null, ct);

    public ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Post, $"/api/backups/restore?name={Uri.EscapeDataString(backupName)}", null, ct);

    public ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Delete, $"/api/backups?name={Uri.EscapeDataString(backupName)}", null, ct);

    // --- Settings ---------------------------------------------------------

    public async ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default)
    {
        var dto = await GetDtoAsync<SettingsDto>("/api/settings", ct);
        return dto!.Sections
            .Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Pairs))
            .ToList();
    }

    public ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Put, $"/api/settings/{Uri.EscapeDataString(section)}/{Uri.EscapeDataString(key)}", new { value }, ct);

    public ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default) =>
        SendJsonAsync(HttpMethod.Post, "/api/settings/restore-defaults", null, ct);

    // --- Request/response plumbing ----------------------------------------

    private async Task<HttpResponseMessage> PostAuthAsync(string url, object? body, CancellationToken ct)
    {
        try
        {
            var response = await SendJsonWithResponseAsync(HttpMethod.Post, url, body, ct);
            return response;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("no connection"))
        {
            AppLogger.Error($"Auth call failed: the host is unreachable", ex);
            throw new HostUnreachableException($"Host is unreachable: {ex.Message}", ex);
        }
    }

    private async ValueTask<T?> GetDtoAsync<T>(string url, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
            return await ReadJsonAsync<T>(response.Content, response.StatusCode, ct);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("no connection"))
        {
            AppLogger.Error($"GET {url} failed: host unreachable", ex);
            throw new HostUnreachableException($"Host is unreachable: {ex.Message}", ex);
        }
    }

    private async ValueTask SendJsonAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var response = await SendJsonWithResponseAsync(method, url, body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendJsonWithResponseAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, Json),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        // Inject Bearer token so /api endpoints resolve the same session identity.
        var token = Token;
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async ValueTask EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        var message = await ReadErrorMessageAsync(response.Content, response.StatusCode, ct);

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

    private static async ValueTask<T?> ReadJsonAsync<T>(HttpContent content, System.Net.HttpStatusCode status, CancellationToken ct)
    {
        var text = await content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException ex)
        {
            AppLogger.Error($"JSON deserialization failed for status {(int)status}", ex);
            throw new HttpRequestException($"{(int)status} unexpected response payload");
        }
    }

    private static async ValueTask<string> ReadErrorMessageAsync(HttpContent content, System.Net.HttpStatusCode status, CancellationToken ct)
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
}
