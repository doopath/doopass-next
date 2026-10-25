using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Doopass.Cli.Api;

/// <summary>Endpoint filter enforcing <c>Authorization: Bearer &lt;token&gt;</c> against the coordinator.</summary>
public sealed class RequireVaultTokenFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var coordinator = context.HttpContext.RequestServices.GetRequiredService<ApiVaultCoordinator>();
        var token = VaultAuth.ParseBearerToken(context.HttpContext);

        if (token is null || coordinator.TouchToken(token) is null)
        {
            return VaultAuth.Unauthorized();
        }

        context.HttpContext.Items[VaultAuth.TokenItemKey] = token;
        return await next(context);
    }
}

/// <summary>Sets <c>Cache-Control: no-store</c> on the whole API group so secret-bearing responses never stick in caches.</summary>
public sealed class NoStoreFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers["Cache-Control"] = "no-store";
        return next(context);
    }
}

/// <summary>Shared helpers for the vault REST API.</summary>
public static class VaultAuth
{
    public const string TokenItemKey = "Doopass.VaultToken";

    public static readonly string[] AuthorizationSchemes = ["Bearer"];

    private static readonly Regex BearerPattern = new("""^Bearer[ ]+(?<token>[A-Za-z0-9_-]+)$""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? ParseBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        var match = BearerPattern.Match(header);
        return match.Success ? match.Groups["token"].Value : null;
    }

    public static IResult Unauthorized() =>
        Results.Json(new { error = "Session expired or unauthorized." },
            statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Fixed-window login limiter, 5 requests per minute per remote IP.</summary>
    public static void AddVaultRateLimiter(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, _) =>
            {
                context.HttpContext.Response.Headers["Cache-Control"] = "no-store";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many authentication attempts. Slow down." });
            };

            options.AddPolicy("vault-auth", static context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });
    }
}
