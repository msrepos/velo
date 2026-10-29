using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Identity.Application;

namespace Velo.Modules.Identity.Endpoints;

public static class UserAdminEndpoints
{
    public const string Permission = "users.manage";
    public static IEndpointRouteBuilder MapUserAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/users").WithTags("User management")
            .RequireAuthorization(policy => policy.RequireRole("Staff").RequireClaim("permission", Permission))
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                try { return await next(context); }
                catch (AuthException error) { return Problem(error.StatusCode, error.Code); }
                catch (DbUpdateConcurrencyException) { return Problem(409, "user_version_conflict"); }
                catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
                { return Problem(409, "user_registration_conflict"); }
            });
        group.MapGet("/", async (IAdminUsers service, CancellationToken ct, string? search = null, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.ListAsync(search, page, pageSize, ct)));
        group.MapPost("/", async (CreateAdminUserRequest request, ClaimsPrincipal actor, IAdminUsers service, CancellationToken ct) =>
        {
            var user = await service.CreateAsync(Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), request, ct);
            return Results.Created($"/api/v1/admin/users/{user.Id}", user);
        });
        group.MapPut("/{id:guid}", async (Guid id, AdminUserRequest request, ClaimsPrincipal actor, IAdminUsers service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), id, request, ct)));
        return endpoints;
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
