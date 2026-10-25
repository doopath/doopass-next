using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Doopass.Core;
using Doopass.WebUi.Abstractions;
using Doopass.WebUi.Services;

namespace Doopass.Mobile.Services;

/// <summary>
/// IVaultSession for the thin client: every call is an HTTP request against the
/// host REST API (Doopass.Cli.Api). HTTP statuses map back to Core exceptions so
/// the shared RCL pages behave exactly like the local web version. Secret
/// responses are never logged and never cached.
/// </summary>
public sealed class RemoteVaultSession : IVaultSession
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly ConnectionStore _connections;
    private readonly VaultEventRelay _relay;
    private HttpClient? _client;
    private string? _clientToken;

    public RemoteVaultSession(ConnectionStore connections, VaultEventRelay relay)
    {
        _connections = connections;
        _relay = relay;
    }

    private HttpClient Client
    {
        get
        {
            if (_client is not null && string.Equals(_clientToken, _connections.Token, StringComparison.Ordinal))
            {
                return _client;
            }

            _client?.Dispose();
            _clientToken = _connections.Token;
            _client = BuildClient();
            return _client;
        }
    }

    public async ValueTask<VaultState> GetStateAsync(CancellationToken ct = default)
    {
        if (!_connections.IsConfigured)
        {
            return new VaultState(false, false, CoreVersion);
        }

        try
        {
            using var response = await Client.GetAsync("/api/vault/state", HttpCompletionOption.ResponseHeadersRead, ct);
            var state = await ReadJsonAsync<StateDto>(response.Content, response.StatusCode, ct);
            return new VaultState(state!.StoreExists, state.Unlocked, state.ServerVersion ?? CoreVersion);
        }
        catch (HttpRequestException ex)
        {
            Diagnostics(ex, "vault/state");
            return new VaultState(false, false, $"{CoreVersion} (offline)");
        }
        catch (PropertyDoesNotExistError)
        {
            // Status parsing never emits this; kept for API shape fidelity.
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new VaultState(false, false, CoreVersion);
        }
    }

    public async ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Post, "/api/auth/login", new { password }, ct);

        if (response.IsSuccessStatusCode)
        {
            var payload = await ReadJsonAsync<TokenDto>(response.Content, response.StatusCode, ct);
            _clientToken = null; // force a client rebuild with the new token
            await _connections.SetTokenAsync(payload!.Token);
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
    }

    public async ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Post, "/api/auth/init", new { password }, ct);

        if (response.IsSuccessStatusCode)
        {
            var payload = await ReadJsonAsync<TokenDto>(response.Content, response.StatusCode, ct);
            _clientToken = null;
            await _connections.SetTokenAsync(payload!.Token);
            return (true, string.Empty);
        }

        return (false, await ReadErrorMessageAsync(response.Content, response.StatusCode, ct));
    }

    public async ValueTask LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SendCoreAsync(HttpMethod.Post, "/api/auth/logout", content: null, ct);
        }
        catch (HttpRequestException ex)
        {
            Diagnostics(ex);
            // A network failure still ends the local session.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _clientToken = null;
        await _connections.SetTokenAsync(null);
    }

    public ValueTask ReloadAsync(CancellationToken ct = default) =>
        ValueTask.CompletedTask; // the remote session always reflects the host

    public async ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default)
    {
        var state = await GetJsonAsync<KeysDto>("/api/vault/keys", ct);
        return state!.Keys;
    }

    public async ValueTask<string> GetValueAsync(string key, CancellationToken ct = default)
    {
        var payload = await GetJsonAsync<ValueDto>($"/api/vault/value?key={Uri.EscapeDataString(key)}", ct);
        return payload!.Value;
    }

    public async ValueTask AddAsync(string key, string value, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/vault/pairs", new { key, value }, ct);

    public async ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, "/api/vault/pairs", new { oldKey, newKey, newValue }, ct);

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/api/vault/pairs?key={Uri.EscapeDataString(key)}", content: null, ct);

    public async ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default)
    {
        var state = await GetJsonAsync<NamesDto>("/api/backups", ct);
        return state!.Names;
    }

    public async ValueTask CreateBackupAsync(CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/backups", content: null, ct);

    public async ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, $"/api/backups/restore?name={Uri.EscapeDataString(backupName)}", content: null, ct);

    public async ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/api/backups?name={Uri.EscapeDataString(backupName)}", content: null, ct);

    public async ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default)
    {
        var state = await GetJsonAsync<SettingsDto>("/api/settings", ct);
        return state!.Sections
            .Select(section => (section.Name, (IReadOnlyDictionary<string, string>)section.Pairs))
            .ToList();
    }

    public async ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, $"/api/settings/{Uri.EscapeDataString(section)}/{Uri.EscapeDataString(key)}", new { value }, ct);

    public async ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "/api/settings/restore-defaults", content: null, ct);

    // --- Request/response plumbing ---------------------------------------

    private async ValueTask<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
            return await ReadJsonAsync<T>(response.Content, response.StatusCode, ct);
        }
        catch (HttpRequestException ex)
        {
            Diagnostics(ex);
            throw new HttpRequestException($"Host is unreachable: {ex.Message}", ex);
        }
    }

    private async ValueTask SendAsync(HttpMethod method, string url, object? content, CancellationToken ct)
    {
        try
        {
            using var response = await SendCoreAsync(method, url, content, ct);
            await EnsureSuccessAsync(response, ct);
        }
        catch (HttpRequestException ex)
        {
            Diagnostics(ex);
            throw new HttpRequestException($"Host is unreachable: {ex.Message}", ex);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);

        if (content is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(content, WebJson),
                Encoding.UTF8,
                "application/json");
        }

        return await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
                // Session gone (logout elsewhere, TTL); reset and surface to the UI.
                _clientToken = null;
                await _connections.SetTokenAsync(null);
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
            return JsonSerializer.Deserialize<T>(text, WebJson);
        }
        catch (JsonException ex)
        {
            Diagnostics(ex);
            throw new HttpRequestException($"{(int)status} {(HttpStatusCode)status}: unexpected response payload");
        }
    }

    private async ValueTask<string> ReadErrorMessageAsync(HttpContent content, HttpStatusCode status, CancellationToken ct)
    {
        try
        {
            var text = await content.ReadAsStringAsync(ct);
            var error = JsonSerializer.Deserialize<ErrorDto>(text, WebJson)?.Error;
            return string.IsNullOrEmpty(error) ? $"{(int)status} {status}" : error;
        }
        catch (JsonException)
        {
            return $"{(int)status} {status}";
        }
    }

    private HttpClient BuildClient()
    {
        var handler = new HttpClientHandler();

        if (_connections.HostUrl?.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(_connections.PinnedFingerprint))
        {
            var pin = _connections.PinnedFingerprint;

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
                return string.Equals(fingerprint, pin, StringComparison.OrdinalIgnoreCase);
            };
        }

        var url = _connections.HostUrl ?? new Uri("http://localhost:7240");

        var client = new HttpClient(handler)
        {
            BaseAddress = url,
            Timeout = TimeSpan.FromSeconds(20),
        };

        client.DefaultRequestHeaders.Authorization =
            string.IsNullOrEmpty(_connections.Token) ? null : new AuthenticationHeaderValue("Bearer", _connections.Token);

        return client;
    }

    private static void Diagnostics(Exception exception, string? context = null) =>
        Doopass.Core.Diagnostics.AppLogger.Error(
            context is null ? "Remote vault call failed" : $"Remote vault call failed: {context}", exception);

    private const string CoreVersion = "5.0.0";

    // JSON field names come from the host as camelCase ("unlocked", not
    // "isUnlocked") — the property name must match the wire name, otherwise the
    // state silently reads as locked and pages bounce between / and /store.
    private sealed record StateDto(bool StoreExists, bool Unlocked, string? ServerVersion, string? CertFingerprint);

    private sealed record TokenDto(string Token, string ServerVersion);

    private sealed record KeysDto(IReadOnlyList<string> Keys);

    private sealed record ValueDto(string Value);

    private sealed record NamesDto(IReadOnlyList<string> Names);

    private sealed record SettingsDto(IReadOnlyList<SettingsSectionDto> Sections);

    private sealed record SettingsSectionDto(string Name, IReadOnlyDictionary<string, string> Pairs);

    private sealed record ErrorDto(string? Error);
}
