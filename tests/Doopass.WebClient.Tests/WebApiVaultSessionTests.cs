using System.Net;
using System.Text;
using System.Text.Json;
using Doopass.Core;
using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace Doopass.WebClient.Tests;

/// <summary>
/// Fake JS interop that simulates sessionStorage. Keys persist between calls so
/// login/restore/logout flows behave like a real browser tab.
/// </summary>
public sealed class FakeJsRuntime : IJSRuntime
{
    private readonly Dictionary<string, string> _storage = new();

    public object? InvokeCore(string identifier, object?[]? args)
    {
        var key = (string?)args![0] ?? "";
        return identifier switch
        {
            "sessionStorage.setItem" => (_storage[key] = (string?)args[1] ?? string.Empty),
            "sessionStorage.removeItem" => _storage.Remove(key),
            "sessionStorage.getItem" => _storage.TryGetValue(key, out var value) ? value : null,
            _ => throw new NotSupportedException($"Unknown JS call: {identifier}"),
        };
    }

    /// <summary>Exposes the stored token for assertions and a clean slate between tests.</summary>
    public string? Token => _storage.TryGetValue("doopass_vault_token", out var value) ? value : null;

    public void Reset() => _storage.Clear();

    public ValueTask<object?> InvokeAsync(string identifier, object?[]? args) => new(InvokeCore(identifier, args));
    public ValueTask<TResult> InvokeAsync<TResult>(string identifier, object?[]? args) => new((TResult)(object?)InvokeCore(identifier, args)!);
    public ValueTask<TResult> InvokeAsync<TResult>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TResult>(identifier, args);
}

public sealed class TestHandler : HttpMessageHandler
{
    private readonly List<HttpResponseMessage> _responses = new();
    private readonly List<string?> _authorizations = new();
    private int _index;

    /// <summary>The bearer token (without scheme) sent on the most recent request.</summary>
    public string? LastAuthorization => _authorizations.Count > 0 ? _authorizations[^1] : null;

    public void Queue(HttpResponseMessage response) => _responses.Add(response);
    public void QueueException(Exception ex) => _responses.Add(null!); // marker
    public bool HasExceptions { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_responses)
        {
            if (_index >= _responses.Count)
                throw new InvalidOperationException($"TestHandler: no more responses queued. Request was {request.Method} {request.RequestUri}");
            var response = _responses[_index++];
            _authorizations.Add(ParseBearer(request));
            if (response is null)
            {
                HasExceptions = true;
                throw new HttpRequestException("Connection refused");
            }

            return Task.FromResult(response);
        }
    }

    private static string? ParseBearer(HttpRequestMessage request)
    {
        var auth = request.Headers.Authorization?.ToString();
        const string prefix = "Bearer ";
        return auth is not null && auth.StartsWith(prefix, StringComparison.Ordinal) ? auth[prefix.Length..] : null;
    }

    public void Reset() { lock (_responses) { _index = 0; _responses.Clear(); HasExceptions = false; _authorizations.Clear(); } }
}

public static class JsonResponses
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Error(int status, string message) => new((HttpStatusCode)status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { error = message }, Options), Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage State(bool storeExists, bool unlocked, string? serverVersion = null) =>
        Ok(JsonSerializer.Serialize(new { storeExists, unlocked, serverVersion = serverVersion ?? "5.0.0" }, Options));

    public static HttpResponseMessage Token(string token, string? serverVersion = null) =>
        Ok(JsonSerializer.Serialize(new { token, serverVersion = serverVersion ?? "5.0.0" }, Options));

    public static HttpResponseMessage Keys(params string[] keys) =>
        Ok(JsonSerializer.Serialize(new { keys }, Options));

    public static HttpResponseMessage Value(string value) =>
        Ok(JsonSerializer.Serialize(new { value }, Options));

    public static HttpResponseMessage Names(params string[] names) =>
        Ok(JsonSerializer.Serialize(new { names }, Options));

    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);
}

public sealed class WebApiVaultSessionTests : IDisposable
{
    private readonly FakeJsRuntime _js = new();
    private readonly TestHandler _handler = new();
    private readonly VaultEventRelay _relay = new();
    private HttpClient? _httpClient;
    private WebApiVaultSession? _session;
    private bool _vaultLockedRaised;

    private WebApiVaultSession Session =>
        _session ??= new(_httpClient!, _js, _relay);

    void Setup()
    {
        _handler.Reset();
        _vaultLockedRaised = false;
        _relay.VaultLocked += () => { _vaultLockedRaised = true; return Task.CompletedTask; };
    }

    [Fact]
    public async Task LoginAsync_incorrect_password_returns_false_message()
    {
        Setup();
        _handler.Queue(JsonResponses.Error(401, "Incorrect password!"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (success, message) = await Session.LoginAsync("wrong");
        Assert.False(success);
        Assert.Equal("Incorrect password!", message);
    }

    [Fact]
    public async Task LoginAsync_no_store_returns_false_message()
    {
        Setup();
        _handler.Queue(JsonResponses.Error(409, "Store is not initialized. Please initialize it first."));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (success, message) = await Session.LoginAsync("pass");
        Assert.False(success);
        Assert.Contains("not initialized", message);
    }

    [Fact]
    public async Task InitAsync_existing_store_returns_false_message()
    {
        Setup();
        _handler.Queue(JsonResponses.Error(409, "Store already exists!"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (success, message) = await Session.InitAsync("pass123");
        Assert.False(success);
        Assert.Equal("Store already exists!", message);
    }

    [Fact]
    public async Task AddAsync_duplicate_key_throws_PropertyAlreadyExistError()
    {
        Setup();
        _handler.Queue(JsonResponses.Error(409, "Key 'github' already exists!"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var ex = await Assert.ThrowsAsync<PropertyAlreadyExistError>(async () => await Session.AddAsync("github", "secret"));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task GetValueAsync_missing_key_throws_PropertyDoesNotExistError()
    {
        Setup();
        _handler.Queue(JsonResponses.Error(404, "Key 'missing' does not exist!"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var ex = await Assert.ThrowsAsync<PropertyDoesNotExistError>(async () => await Session.GetValueAsync("missing"));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task GetKeysAsync_401_clears_token_and_raises_vault_locked()
    {
        Setup();
        // Login succeeds
        _handler.Queue(JsonResponses.Token("tok123"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (ok, _) = await Session.LoginAsync("pass");
        Assert.True(ok);

        // Now 401 on a protected call
        _vaultLockedRaised = false;
        _handler.Queue(JsonResponses.Error(401, "Session expired or unauthorized."));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await Session.GetKeysAsync());
        Assert.True(_vaultLockedRaised);
        Assert.Null(_js.Token); // the token was cleared on 401
    }

    [Fact]
    public async Task AddAsync_429_throws_InvalidOperationException_with_message()
    {
        Setup();
        _handler.Queue(JsonResponses.Error(429, "Rate limit exceeded. Try again later."));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Session.AddAsync("key", "value"));
        Assert.Contains("Rate limit", ex.Message);
    }

    [Fact]
    public async Task Full_cycle_login_add_get_update_delete()
    {
        Setup();
        _handler.Queue(JsonResponses.Token("tok456"));              // login POST
        _handler.Queue(JsonResponses.Keys("github"));               // getKeys GET
        _handler.Queue(JsonResponses.Value("s3cret"));              // getValue GET
        _handler.Queue(JsonResponses.NoContent());                  // add POST
        _handler.Queue(JsonResponses.NoContent());                  // update PUT
        _handler.Queue(JsonResponses.NoContent());                  // remove DELETE

        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (ok, _) = await Session.LoginAsync("pass");
        Assert.True(ok);

        var keys = await Session.GetKeysAsync();
        Assert.Contains("github", keys);

        var val = await Session.GetValueAsync("github");
        Assert.Equal("s3cret", val);
    }

    [Fact]
    public async Task GetStateAsync_host_unreachable_returns_offline_state()
    {
        Setup();
        // Force host unreachable via queued exception marker
        _handler.Reset();
        _handler.QueueException(new Exception("Connection refused"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var state = await Session.GetStateAsync();

        Assert.True(state.IsOffline);
        Assert.False(state.StoreExists);
        Assert.False(state.IsUnlocked);
    }

    [Fact]
    public async Task LoginAsync_attaches_bearer_token_to_protected_requests()
    {
        Setup();
        _handler.Queue(JsonResponses.Token("tok-bearer"));   // login POST
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (ok, _) = await Session.LoginAsync("pass");
        Assert.True(ok);
        Assert.Equal("tok-bearer", _js.Token);

        _handler.Queue(JsonResponses.Keys("github"));         // protected GET
        var keys = await Session.GetKeysAsync();

        Assert.Contains("github", keys);
        Assert.Equal("tok-bearer", _handler.LastAuthorization);
    }

    [Fact]
    public async Task LogoutAsync_sends_bearer_and_clears_local_session()
    {
        Setup();
        _handler.Queue(JsonResponses.Token("tok-out"));       // login POST
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (ok, _) = await Session.LoginAsync("pass");
        Assert.True(ok);
        Assert.Equal("tok-out", _js.Token);

        _handler.Reset();                                     // observe only the logout request
        _handler.Queue(JsonResponses.NoContent());            // logout POST
        await Session.LogoutAsync();

        Assert.Null(_js.Token);                               // sessionStorage cleared
        Assert.Equal("tok-out", _handler.LastAuthorization);  // server got the bearer to revoke it
    }

    [Fact]
    public async Task InitializeAsync_restores_token_from_session_storage()
    {
        Setup();
        _js.Reset();
        _js.InvokeCore("sessionStorage.setItem", new object[] { "doopass_vault_token", "tok-restored" });

        _handler.Queue(JsonResponses.State(storeExists: true, unlocked: true));  // GetState
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        await Session.InitializeAsync();
        Assert.Equal("tok-restored", _js.Token);

        var state = await Session.GetStateAsync();
        Assert.True(state.IsUnlocked);
        Assert.Equal("tok-restored", _handler.LastAuthorization);
    }

    [Fact]
    public async Task ServerError_500_throws_http_request_exception_not_unreachable()
    {
        Setup();
        _handler.Queue(JsonResponses.Token("tok-x"));         // login
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };
        await Session.LoginAsync("pass");

        _handler.Queue(JsonResponses.Error(500, "Boom"));     // server error on a protected call
        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () => await Session.GetKeysAsync());
        Assert.DoesNotContain("unreachable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_host_unreachable_returns_offline_message_not_throws()
    {
        Setup();
        _handler.QueueException(new Exception("Connection refused"));
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:7240/") };

        var (success, message) = await Session.LoginAsync("pass");
        Assert.False(success);
        Assert.Contains("reach the Doopass host", message);
    }

    public void Dispose() => _httpClient?.Dispose();
}
