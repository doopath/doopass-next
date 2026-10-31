using System.Net.Http.Headers;
using System.Text.Json;
using Doopass.Cli.Api;
using Microsoft.JSInterop;

namespace Doopass.Cli.Services;

/// <summary>
/// Browser-side HTTP client via JS interop. Makes fetch() calls from the browser
/// so that HTTP requests to /api/* originate from the browser (visible in the
/// Network tab). Used by LocalApiService when running in a Blazor Server circuit.
/// </summary>
internal sealed class BrowserHttpClient : IDisposable
{
    private readonly IJSRuntime? _js;
    private readonly HttpClient? _httpClient;
    private readonly ApiVaultCoordinator? _coordinator;
    private string? _localToken;
    private IJSObjectReference? _module;

    /// <summary>
    /// Production constructor: uses JS interop. Token comes from localStorage.
    /// </summary>
    public BrowserHttpClient(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Test constructor: wraps an HttpClient instead of using JS interop.
    /// Token comes from the coordinator or a local override.
    /// </summary>
    public BrowserHttpClient(IHttpClientFactory factory, int port, ApiVaultCoordinator coordinator)
    {
        _httpClient = factory.CreateClient();
        _httpClient.BaseAddress = new Uri($"http://localhost:{port}/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _coordinator = coordinator;
    }

    /// <summary>
    /// Sets a local token override (used by LocalApiService after login/init).
    /// </summary>
    public void SetLocalToken(string? token)
    {
        _localToken = token;
    }

    private async ValueTask<IJSObjectReference> ModuleAsync()
    {
        if (_module is not null) return _module;
        var js = _js ?? throw new InvalidOperationException("BrowserHttpClient not initialized with IJSRuntime");
        _module = await js.InvokeAsync<IJSObjectReference>("import", "./doopass-api.js");
        return _module;
    }

    private string? GetToken()
    {
        // Priority: local override (set by LocalApiService after login) > coordinator > null
        return _localToken ?? _coordinator?.ActiveToken;
    }

    public async ValueTask<HttpResponse> GetAsync(string url, CancellationToken ct = default)
    {
        if (_httpClient is not null)
        {
            var token = GetToken();
            if (token is not null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            var serverRes = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return await ToHttpResponse(serverRes, ct);
        }
        var mod = await ModuleAsync();
        var browserRes = await mod.InvokeAsync<HttpResponse>("doopass.api.get", url);
        return browserRes;
    }

    public async ValueTask<HttpResponse> PostAsync(string url, object? body, CancellationToken ct = default)
    {
        if (_httpClient is not null)
        {
            var token = GetToken();
            if (token is not null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            var content = body is not null
                ? new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json")
                : null;
            var serverRes = await _httpClient.PostAsync(url, content, ct);
            return await ToHttpResponse(serverRes, ct);
        }
        var mod = await ModuleAsync();
        var browserRes = await mod.InvokeAsync<HttpResponse>("doopass.api.post", url, body);
        return browserRes;
    }

    public async ValueTask<HttpResponse> PutAsync(string url, object? body, CancellationToken ct = default)
    {
        if (_httpClient is not null)
        {
            var token = GetToken();
            if (token is not null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            var content = body is not null
                ? new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json")
                : null;
            var serverRes = await _httpClient.PutAsync(url, content, ct);
            return await ToHttpResponse(serverRes, ct);
        }
        var mod = await ModuleAsync();
        var browserRes = await mod.InvokeAsync<HttpResponse>("doopass.api.put", url, body);
        return browserRes;
    }

    public async ValueTask<HttpResponse> DeleteAsync(string url, CancellationToken ct = default)
    {
        if (_httpClient is not null)
        {
            var token = GetToken();
            if (token is not null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            var serverRes = await _httpClient.DeleteAsync(url, ct);
            return await ToHttpResponse(serverRes, ct);
        }
        var mod = await ModuleAsync();
        var browserRes = await mod.InvokeAsync<HttpResponse>("doopass.api.delete", url);
        return browserRes;
    }

    public async ValueTask<string?> GetTokenAsync(CancellationToken ct = default)
    {
        if (_httpClient is not null) return null; // tests don't need token
        var mod = await ModuleAsync();
        return await mod.InvokeAsync<string?>("doopass.api.token.get");
    }

    public async ValueTask SetTokenAsync(string? token, CancellationToken ct = default)
    {
        if (_httpClient is not null)
        {
            SetLocalToken(token); // tests: use local override
            return;
        }
        var mod = await ModuleAsync();
        await mod.InvokeVoidAsync("doopass.api.token.set", token);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        if (_module is not null)
        {
            _ = _module.DisposeAsync();
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async ValueTask<HttpResponse> ToHttpResponse(HttpResponseMessage msg, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>();
        foreach (var h in msg.Headers)
        {
            headers[h.Key] = string.Join(",", h.Value);
        }
        return new HttpResponse(msg.IsSuccessStatusCode, (int)msg.StatusCode, headers, await msg.Content.ReadAsStringAsync(ct));
    }
}

/// <summary>Minimal HTTP response DTO from the JS bridge.</summary>
internal sealed record HttpResponse(bool Ok, int Status, Dictionary<string, string> Headers, string Body);
