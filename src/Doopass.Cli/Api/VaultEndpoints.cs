using Doopass.Core;
using Doopass.WebUi.Abstractions;

namespace Doopass.Cli.Api;

/// <summary>
/// REST surface of the vault host for thin clients. Auth is
/// <c>Authorization: Bearer &lt;token&gt;</c> everywhere except
/// <c>GET /api/vault/state</c>. Keys and backup names travel as query
/// parameters or JSON bodies — never path segments — because they may contain
/// '/', '=' and unicode. All endpoints in the group get
/// <c>Cache-Control: no-store</c>; errors are always
/// <c>{ "error": "&lt;message&gt;" }</c>.
/// </summary>
public static class VaultEndpoints
{
    private sealed record PasswordRequest(string Password);

    private sealed record PairRequest(string Key, string Value);

    private sealed record PairUpdateRequest(string OldKey, string NewKey, string NewValue);

    private sealed record SettingValueRequest(string Value);

    /// <summary>Maps the whole <c>/api</c> group (auth, vault, backups, settings) onto the pipeline.</summary>
    /// <param name="app">Endpoint route builder of the web host.</param>
    /// <returns>The group, for further conventions if needed.</returns>
    public static IEndpointConventionBuilder MapDoopassApi(this IEndpointRouteBuilder app)
    {
        var coordinator = app.ServiceProvider.GetRequiredService<ApiVaultCoordinator>();

        var group = app.MapGroup("/api")
            .AddEndpointFilter<NoStoreFilter>();

        var announce = app.ServiceProvider.GetRequiredService<DiscoveryAnnounce>();
        var secure = group
            .MapGroup("")
            .AddEndpointFilter<RequireVaultTokenFilter>();

        // --- Auth -----------------------------------------------------------

        group.MapPost("/auth/login", (PasswordRequest? body) =>
            Guard(() =>
            {
                if (body is null || string.IsNullOrEmpty(body.Password))
                {
                    return Error(StatusCodes.Status400BadRequest, "Password is required!");
                }

                var (success, token, error) = coordinator.Login(body.Password);

                return success && token is not null
                    ? Results.Json(new { token, serverVersion = coordinator.ServerVersion })
                    : Error(CanonicalStatus(error), error!);
            }))
            .RequireRateLimiting("vault-auth")
            .AddEndpointFilter<NoStoreFilter>();

        group.MapPost("/auth/init", (PasswordRequest? body) =>
            Guard(() =>
            {
                if (body is null || string.IsNullOrEmpty(body.Password))
                {
                    return Error(StatusCodes.Status400BadRequest, "Password is required!");
                }

                var (success, token, error) = coordinator.Init(body.Password);

                return success && token is not null
                    ? Results.Json(new { token, serverVersion = coordinator.ServerVersion })
                    : Error(CanonicalStatus(error), error!);
            }))
            .RequireRateLimiting("vault-auth")
            .AddEndpointFilter<NoStoreFilter>();

        secure.MapPost("/auth/logout", (HttpContext ctx) =>
            Guard(() =>
            {
                coordinator.Logout(GetToken(ctx));
                return Results.NoContent();
            }));

        // --- Vault state / pairs --------------------------------------------

        group.MapGet("/vault/state", () => Results.Json(new
        {
            storeExists = coordinator.GetState().StoreExists,
            unlocked = coordinator.GetState().IsUnlocked,
            serverVersion = coordinator.ServerVersion,
            certFingerprint = announce.Fingerprint,
        }));

        secure.MapGet("/vault/keys", (HttpContext ctx) => Guard(() =>
            Results.Json(new { keys = coordinator.GetKeys(GetToken(ctx)) })));

        secure.MapGet("/vault/value", (HttpContext ctx, string key) => Guard(() =>
            Results.Json(new { value = coordinator.GetValue(GetToken(ctx), key) })));

        secure.MapPost("/vault/pairs", (HttpContext ctx, PairRequest? body) => Guard(() =>
        {
            if (body is null || string.IsNullOrEmpty(body.Key))
            {
                return Error(StatusCodes.Status400BadRequest, "Key is required.");
            }

            coordinator.Add(GetToken(ctx), body.Key, body.Value ?? string.Empty);
            return Results.NoContent();
        }));

        secure.MapPut("/vault/pairs", (HttpContext ctx, PairUpdateRequest? body) => Guard(() =>
        {
            if (body is null || string.IsNullOrEmpty(body.OldKey) || string.IsNullOrEmpty(body.NewKey))
            {
                return Error(StatusCodes.Status400BadRequest, "Both oldKey and newKey are required.");
            }

            coordinator.Update(GetToken(ctx), body.OldKey, body.NewKey, body.NewValue ?? string.Empty);
            return Results.NoContent();
        }));

        secure.MapDelete("/vault/pairs", (HttpContext ctx, string key) => Guard(() =>
        {
            coordinator.Remove(GetToken(ctx), key);
            return Results.NoContent();
        }));

        secure.MapPost("/vault/reload", (HttpContext ctx) => Guard(() =>
        {
            coordinator.Reload(GetToken(ctx));
            return Results.NoContent();
        }));

        // --- Backups ----------------------------------------------------------

        secure.MapGet("/backups", (HttpContext ctx) => Guard(() =>
            Results.Json(new { names = coordinator.GetBackupNames(GetToken(ctx)) })));

        secure.MapPost("/backups", (HttpContext ctx) => Guard(() =>
        {
            coordinator.CreateBackup(GetToken(ctx));
            return Results.NoContent();
        }));

        secure.MapPost("/backups/restore", (HttpContext ctx, string name) => Guard(() =>
        {
            coordinator.RestoreBackup(GetToken(ctx), name);
            return Results.NoContent();
        }));

        secure.MapDelete("/backups", (HttpContext ctx, string name) => Guard(() =>
        {
            coordinator.DeleteBackup(GetToken(ctx), name);
            return Results.NoContent();
        }));

        // --- Settings ---------------------------------------------------------

        secure.MapGet("/settings", (HttpContext ctx) => Guard(() =>
            Results.Json(new
            {
                sections = coordinator
                    .GetSettings(GetToken(ctx))
                    .Select(section => new { name = section.Section, pairs = section.Pairs }),
            })));

        secure.MapPut("/settings/{section}/{key}", (HttpContext ctx, string section, string key, SettingValueRequest? body) =>
            Guard(() =>
            {
                coordinator.SetSetting(GetToken(ctx), section, key, body?.Value ?? string.Empty);
                return Results.NoContent();
            }));

        secure.MapPost("/settings/restore-defaults", (HttpContext ctx) => Guard(() =>
        {
            coordinator.RestoreDefaultSettings(GetToken(ctx));
            return Results.NoContent();
        }));

        return group;
    }

    private static string GetToken(HttpContext ctx) =>
        ctx.Items[VaultAuth.TokenItemKey] as string ?? string.Empty;

    private static IResult Guard(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return MapStoreError(ex);
        }
    }

    private static IResult MapStoreError(Exception ex)
    {
        var (status, message) = ex switch
        {
            PropertyAlreadyExistError => (StatusCodes.Status409Conflict, ex.Message),
            PropertyDoesNotExistError => (StatusCodes.Status404NotFound, ex.Message),
            IncorrectPasswordError => (StatusCodes.Status401Unauthorized, ex.Message),
            StoreIsNotInitializedError => (StatusCodes.Status409Conflict, ex.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Session expired or unauthorized."),
            _ => (StatusCodes.Status500InternalServerError, ex.Message),
        };

        return Error(status, message);
    }

    /// <summary>Auth outcomes carry their message in the coordinator result; missing store is a conflict.</summary>
    private static int CanonicalStatus(string? error) =>
        error is null or "Incorrect password!" ? StatusCodes.Status401Unauthorized
        : error.Contains("already exists", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status409Conflict
        : error.Contains("not initialized", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status409Conflict
        : StatusCodes.Status400BadRequest;

    private static IResult Error(int status, string message) =>
        Results.Json(new { error = message }, statusCode: status);
}
