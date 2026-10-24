using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Delivery.Infrastructure;
namespace Velo.Modules.Delivery.Endpoints;
public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/delivery").WithTags("Delivery management")
            .RequireAuthorization(DeliveryPolicies.Manage).AddEndpointFilter(Errors);
        admin.MapPost("/drivers", async (CreateDriverRequest request, DeliveryService service, CancellationToken ct) =>
        { var result = await service.CreateDriverAsync(request, ct); return Results.Created($"/api/v1/admin/delivery/drivers/{result.Id}", result); });
        admin.MapPut("/drivers/{id:guid}", async (Guid id, UpdateDriverRequest request, DeliveryService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateDriverAsync(id, request, ct)));
        admin.MapGet("/drivers", async (DeliveryService service, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.DriversAsync(page, pageSize, ct)));
        var driver = endpoints.MapGroup("/api/v1/driver").WithTags("Driver").RequireAuthorization().AddEndpointFilter(Errors);
        driver.MapGet("/deliveries", async (ClaimsPrincipal u, DeliveryService service, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.OwnJobsAsync(User(u), page, pageSize, ct)));
        driver.MapPost("/location", async (LocationRequest request, ClaimsPrincipal u, DeliveryService service, CancellationToken ct) =>
        { await service.LocationAsync(User(u), request, ct); return Results.NoContent(); });
        var customer = endpoints.MapGroup("/api/v1/orders").WithTags("Tracking").RequireAuthorization().AddEndpointFilter(Errors);
        customer.MapGet("/{id:guid}/tracking", async (Guid id, ClaimsPrincipal u, DeliveryService service, CancellationToken ct) =>
            Results.Ok(await service.TrackingAsync(User(u), id, ct)));
        customer.MapGet("/{id:guid}/delivery-code", async (Guid id, ClaimsPrincipal u, DeliveryService service, CancellationToken ct) =>
            Results.Ok(await service.CustomerCodeAsync(User(u), id, ct)));
        return endpoints;
    }
    private static Guid User(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async ValueTask<object?> Errors(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        c.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return await next(c); }
        catch (DeliveryException e)
        {
            if (e.RetryAfter is { } seconds) c.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
            return Problem(e.Status, e.Code);
        }
        catch (DbUpdateConcurrencyException) { return Problem(409, "delivery_version_conflict"); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) { return Problem(409, "delivery_conflict"); }
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
