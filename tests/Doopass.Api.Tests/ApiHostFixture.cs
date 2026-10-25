using System.Net.Http.Json;
using Doopass.Cli.Api;
using Doopass.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// Builds the real web host on 127.0.0.1:0 with its own DOOPASS_HOME (fresh
/// settings.json and empty store), then exposes an HttpClient plus the built
/// coordinator for direct clock manipulation.
/// </summary>
public sealed class ApiHostFixture : IAsyncLifetime
{
    private string? _home;
    private string? _previousHome;

    public WebApplication App { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public ApiVaultCoordinator Coordinator => App.Services.GetRequiredService<ApiVaultCoordinator>();

    /// <summary>Fresh state per test: no tokens, no unlocked store, no files on disk.</summary>
    public void Reset()
    {
        Coordinator.TestReset();
    }

    public async Task InitializeAsync()
    {
        // The fixtures run sequentially; still remember and restore the variable.
        _previousHome = Environment.GetEnvironmentVariable("DOOPASS_HOME");
        _home = Path.Combine(Path.GetTempPath(), "doopass-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        AppPathsFixtureSetter.SetHome(_home);

        App = WebHostFactory.BuildWebHost(new WebHostOptions
        {
            Port = 0,
            Host = "127.0.0.1",
            OpenBrowser = false,
        });

        await App.StartAsync();

        var address = App.Urls.First(address => address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
        var port = int.Parse(address.Split(':')[^1], System.Globalization.CultureInfo.InvariantCulture);
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();

        if (App is not null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }

        // Test files live in the temp root; deleting is best-effort (log files
        // may linger until process exit). Restore the variable either way.
        AppPathsFixtureSetter.SetHome(_previousHome);

        if (_home is not null)
        {
            try
            {
                Directory.Delete(_home, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public async Task<(string Token, HttpResponseMessage Response)> InitAsync(string password)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/init", new { password });
        var token = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return (token?.Token ?? string.Empty, response);
    }

    public async Task<string> LoginAsync(string password)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { password });
        var token = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return token?.Token ?? string.Empty;
    }

    private sealed record LoginResponse(string Token, string ServerVersion);
}

/// <summary>Keeps the DOOPASS_HOME environment variable aligned with the fixture root.</summary>
internal static class AppPathsFixtureSetter
{
    public static void SetHome(string? path) => Environment.SetEnvironmentVariable("DOOPASS_HOME", path);
}
