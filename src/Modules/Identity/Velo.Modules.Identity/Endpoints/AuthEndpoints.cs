using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure;

namespace Velo.Modules.Identity.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication")
            .RequireRateLimiting("auth")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.HttpContext.Response.Headers.Pragma = "no-cache";
                try { return await next(context); }
                catch (AuthException error)
                {
                    if (error.RetryAfter is { } seconds)
                        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                    return Results.Problem(statusCode: error.StatusCode, title: error.Code,
                        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
                }
            });

        group.MapPost("/register", async (RegisterRequest request, HttpContext http,
            AuthService auth, CancellationToken ct) =>
                Results.Ok(await auth.RequestOtpAsync(request.PhoneNumber,
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct, request))).AllowAnonymous();

        group.MapPost("/otp/request", async (RequestOtpRequest request, HttpContext http,
            AuthService auth, CancellationToken ct) =>
                Results.Ok(await auth.RequestOtpAsync(request.PhoneNumber,
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct))).AllowAnonymous();

        group.MapPost("/otp/verify", async (VerifyOtpRequest request, HttpContext http,
            AuthService auth, CancellationToken ct) =>
                Results.Ok(await auth.VerifyOtpAsync(request.ChallengeId, request.Code,
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct))).AllowAnonymous();

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
            Results.Ok(await auth.RefreshAsync(request.RefreshToken, ct))).AllowAnonymous();
        group.MapPost("/staff/exchange", async (ClaimsPrincipal user, StaffAccessService staff, CancellationToken ct) =>
            Results.Ok(await staff.ExchangeAsync(UserId(user), SessionId(user), ct))).RequireAuthorization();
        group.MapGet("/staff/me", async (ClaimsPrincipal user, StaffAccessService staff, CancellationToken ct) =>
            Results.Ok(new { userId = UserId(user), permissions = await staff.PermissionsAsync(UserId(user), ct) }))
            .RequireAuthorization(policy => policy.RequireRole("Staff"));

        group.MapGet("/me", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
            Results.Ok(await auth.GetCurrentUserAsync(UserId(user), ct))).RequireAuthorization();

        group.MapPost("/logout", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeAsync(UserId(user), SessionId(user), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapPost("/logout-all", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeAllAsync(UserId(user), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/sessions", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
            Results.Ok(await auth.GetSessionsAsync(UserId(user), SessionId(user), ct))).RequireAuthorization();

        group.MapDelete("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal user,
            AuthService auth, CancellationToken ct) =>
        {
            await auth.RevokeAsync(UserId(user), id, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid SessionId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(SessionTokens.SessionClaim)!);
}
