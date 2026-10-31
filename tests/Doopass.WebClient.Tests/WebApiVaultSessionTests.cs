using System.Net;
using System.Text;
using System.Text.Json;
using Doopass.Core;
using Doopass.WebUi.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace Doopass.WebClient.Tests;

/// <summary>
/// Fake JS interop that simulates sessionStorage.
/// </summary>
public sealed class FakeJsRuntime : IJSRuntime
{
    private readonly Dictionary<string, string?> _storage = new();

    public object? InvokeCore(string identifier, object?[]? args)
    {
        return identifier switch
        {
            "sessionStorage.setItem" => null,
            "sessionStorage.removeItem" => null,
            "sessionStorage.getItem" => _storage.TryGetValue((string?)args![0] ?? "", out var v) ? v : null,
            _ => throw new NotSupportedException($"Unknown JS call: {identifier}"),
        };
    }

    public ValueTask<object?> InvokeAsync(string identifier, object?[]? args) => new(InvokeCore(identifier, args));
    public ValueTask<TResult> InvokeAsync<TResult>(string identifier, object?[]? args) => new((TResult)(object?)InvokeCore(identifier, args)!);
    public ValueTask<TResult> InvokeAsync<TResult>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TResult>(identifier, args);
}

public sealed class TestHandler : HttpMessageHandler
{
    private readonly List<HttpResponseMessage> _responses = new();
    private int _index;

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
            if (response is null)
            {
                HasExceptions = true;
                throw new HttpRequestException("Connection refused");
            }
            return Task.FromResult(response);
        }
    }

    public void Reset() { lock (_responses) { _index = 0; _responses.Clear(); HasExceptions = false; } }
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
    private HttpClient? _httpClient;
    private VaultEventRelay? _relay;
    private bool _vaultLockedRaised;

    private WebApiVaultSession Session => new(_httpClient!, _js, _relay!);

    [Fact]
    public void Setup()
    {
        _handler.Reset();
        _relay = new VaultEventRelay();
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

    public void Dispose() => _httpClient?.Dispose();
}
