using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Infrastructure;
namespace Velo.Modules.Inventory.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/inventory").WithTags("Inventory")
            .RequireAuthorization(InventoryPolicies.Manage).AddEndpointFilter(Errors);
        group.MapGet("/warehouses", async (InventoryService service, CancellationToken ct,
            int page = 1, int pageSize = 20) => Results.Ok(await service.WarehousesAsync(page, pageSize, ct)));
        group.MapGet("/warehouses/{id:guid}", async (Guid id, InventoryService service, CancellationToken ct) =>
            Results.Ok(await service.WarehouseAsync(id, ct)));
        group.MapPost("/warehouses", async (WarehouseRequest request, InventoryService service, CancellationToken ct) =>
        {
            var item = await service.SaveWarehouseAsync(null, request, ct);
            return Results.Created($"/api/v1/admin/inventory/warehouses/{item.Id}", item);
        });
        group.MapPut("/warehouses/{id:guid}", async (Guid id, WarehouseRequest request,
            InventoryService service, CancellationToken ct) => Results.Ok(await service.SaveWarehouseAsync(id, request, ct)));
        group.MapGet("/warehouses/{id:guid}/balances", async (Guid id, InventoryService service,
            CancellationToken ct, int page = 1, int pageSize = 20) =>
                Results.Ok(await service.BalancesAsync(id, page, pageSize, ct)));
        group.MapGet("/warehouses/{id:guid}/balances/{variantId:guid}", async (Guid id, Guid variantId,
            InventoryService service, CancellationToken ct) => Results.Ok(await service.GetBalanceAsync(id, variantId, ct)));
        group.MapGet("/warehouses/{id:guid}/movements", async (Guid id, InventoryService service,
            CancellationToken ct, Guid? variantId = null, int page = 1, int pageSize = 20) =>
                Results.Ok(await service.MovementsAsync(id, variantId, page, pageSize, ct)));
        group.MapPost("/warehouses/{id:guid}/adjustments", async (Guid id, AdjustmentRequest request,
            ClaimsPrincipal user, InventoryService service, CancellationToken ct) =>
                Results.Ok(await service.AdjustAsync(id, request, Actor(user), ct)));
        group.MapPost("/transfers", async (TransferRequest request, ClaimsPrincipal user,
            InventoryService service, CancellationToken ct) => Results.Ok(await service.TransferAsync(request, Actor(user), ct)));
        group.MapPost("/reservations", async (ReservationRequest request, ClaimsPrincipal user,
            InventoryService service, CancellationToken ct) => Results.Ok(await service.ReserveAsync(request, Actor(user), ct)));
        group.MapGet("/reservations/{id:guid}", async (Guid id, InventoryService service, CancellationToken ct) =>
            Results.Ok(await service.ReservationAsync(id, ct)));
        group.MapPost("/reservations/{id:guid}/commit", async (Guid id, ClaimsPrincipal user,
            InventoryService service, CancellationToken ct) => Results.Ok(await service.FinishAsync(id, true, Actor(user), ct)));
        group.MapPost("/reservations/{id:guid}/release", async (Guid id, ClaimsPrincipal user,
            InventoryService service, CancellationToken ct) => Results.Ok(await service.FinishAsync(id, false, Actor(user), ct)));
        return endpoints;
    }
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async ValueTask<object?> Errors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return await next(context); }
        catch (InventoryException error) { return Problem(error.Status, error.Code); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "inventory_version_conflict"); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { return Problem(409, "duplicate_inventory_value"); }
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
