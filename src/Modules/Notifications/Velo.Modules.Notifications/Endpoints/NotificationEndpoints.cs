using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Velo.Modules.Notifications.Infrastructure;
namespace Velo.Modules.Notifications.Endpoints;
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization()
            .AddEndpointFilter(async (c, next) =>
            {
                c.HttpContext.Response.Headers.CacheControl = "no-store";
                try { return await next(c); }
                catch (ArgumentOutOfRangeException) { return Results.Problem(statusCode: 400, title: "invalid_pagination"); }
            });
        group.MapGet("/", async (ClaimsPrincipal u, NotificationService service, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.ListAsync(Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!), page, pageSize, ct)));
        group.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal u, NotificationService service, CancellationToken ct) =>
            await service.ReadAsync(Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!), id, ct) ? Results.NoContent() : Results.NotFound());
        return endpoints;
    }
}
