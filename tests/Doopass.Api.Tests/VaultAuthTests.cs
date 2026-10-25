using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// Auth surface: anonymous state and init/login flows. Each test resets the
/// host state; test methods run in name order and the five login-limiter
/// permits cover exactly the auth attempts of T02…T05, so this class must not
/// grow another rate-limited auth call.
/// </summary>
[TestCaseOrderer("Doopass.Api.Tests.AlphabeticalTestCaseOrderer", "Doopass.Api.Tests")]
public sealed class VaultAuthTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_State_is_anonymous_and_reports_missing_store()
    {
        fixture.Reset();

        var response = await fixture.Client.GetAsync("/api/vault/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<State>();
        Assert.False(state!.StoreExists);
        Assert.False(state.IsUnlocked);
        Assert.Equal(Doopass.Core.Constants.Version, state.ServerVersion);
    }

    [Fact]
    public async Task T02_Login_before_init_returns_conflict()
    {
        fixture.Reset();

        var response = await fixture.Client.PostAsJsonAsync("/api/auth/login", new { password = "correct horse" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not initialized", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T03_Init_creates_store_and_issues_token()
    {
        fixture.Reset();
        var (token, response) = await fixture.InitAsync("Very strong 1234");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(string.Empty, token);
    }

    [Fact]
    public async Task T04_Init_twice_returns_conflict()
    {
        await fixture.InitAsync("Very strong 1234");
        var (_, response) = await fixture.InitAsync("Another 1234");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T05_Login_with_wrong_password_is_401()
    {
        // The store from T04 is still there; a bad login must return 401.
        var response = await fixture.Client.PostAsJsonAsync("/api/auth/login", new { password = "wrong 1234" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Incorrect password!", body);
    }

    private sealed record State(bool StoreExists, bool IsUnlocked, string ServerVersion);
}

/// <summary>Init with a bad master password surfaces the validator message as 400.</summary>
public sealed class VaultValidatorTests(ApiHostFixture fixture) : IClassFixture<ApiHostFixture>
{
    [Fact]
    public async Task T01_Init_rejects_validator_messages_with_400()
    {
        fixture.Reset();
        var (_, response) = await fixture.InitAsync(" "); // starts with a space

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("space", body, StringComparison.OrdinalIgnoreCase);
    }
}
