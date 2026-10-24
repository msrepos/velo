using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Velo.Modules.Inventory.Contracts;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Infrastructure;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Payments.Contracts;
namespace Velo.Modules.Orders.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/api/v1/orders").WithTags("Orders").RequireAuthorization().AddEndpointFilter(Errors);
        customer.MapPost("/checkout/preview", async (CheckoutPreviewRequest request, ClaimsPrincipal user,
            OrderService service, CancellationToken ct) => Results.Ok(await service.PreviewAsync(UserId(user), request, ct)));
        customer.MapPost("/checkout", async (CheckoutRequest request, ClaimsPrincipal user, HttpContext http,
            OrderService service, CancellationToken ct) =>
        {
            var result = await service.CheckoutAsync(UserId(user), request, http.Request.Headers.IfMatch, ct);
            return result.Replayed ? Results.Ok(result.Order) : Results.Created($"/api/v1/orders/{result.Order.Id}", result.Order);
        });
        customer.MapGet("/", async (ClaimsPrincipal user, OrderService service, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.ListAsync(UserId(user), page, pageSize, ct)));
        customer.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(UserId(user), id, ct)));
        customer.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrderRequest request, ClaimsPrincipal user,
            OrderService service, CancellationToken ct) => Results.Ok(await service.CancelAsync(UserId(user), id, request, ct)));
        var admin = endpoints.MapGroup("/api/v1/admin/orders").WithTags("Order management")
            .RequireAuthorization(OrderPolicies.Manage).AddEndpointFilter(Errors);
        admin.MapGet("/", async (OrderService service, CancellationToken ct, string? status = null, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.AdminListAsync(status, page, pageSize, ct)));
        admin.MapGet("/{id:guid}", async (Guid id, OrderService service, CancellationToken ct) => Results.Ok(await service.AdminGetAsync(id, ct)));
        admin.MapPost("/{id:guid}/confirm", async (Guid id, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.ConfirmAsync(id, UserId(user), ct)));
        admin.MapPost("/{id:guid}/prepare", async (Guid id, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.PrepareAsync(id, UserId(user), false, ct)));
        admin.MapPost("/{id:guid}/ready", async (Guid id, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.PrepareAsync(id, UserId(user), true, ct)));
        admin.MapPost("/{id:guid}/assign", async (Guid id, AssignDriverRequest request, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.AssignAsync(id, UserId(user), request, ct)));
        admin.MapPost("/{id:guid}/cancel", async (Guid id, AdminCancelRequest request, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.AdminCancelAsync(UserId(user), id, request, ct)));
        admin.MapPost("/{id:guid}/return-to-warehouse", async (Guid id, ReturnOrderRequest request, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.ReturnAsync(UserId(user), id, request, ct)));
        var driver = endpoints.MapGroup("/api/v1/driver/deliveries").WithTags("Driver workflow").RequireAuthorization().AddEndpointFilter(Errors);
        driver.MapPost("/{id:guid}/dispatch", async (Guid id, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.DispatchAsync(UserId(user), id, ct)));
        driver.MapPost("/{id:guid}/complete", async (Guid id, CompleteDeliveryRequest request, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.CompleteAsync(UserId(user), id, request, ct)));
        driver.MapPost("/{id:guid}/fail", async (Guid id, DeliveryFailureRequest request, ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
            Results.Ok(await service.FailDeliveryAsync(UserId(user), id, request, ct)));
        return endpoints;
    }
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async ValueTask<object?> Errors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return await next(context); }
        catch (OrderException e) { return Problem(e.Status, e.Code); }
        catch (StockException e) { return Problem(e.Status, e.Code); }
        catch (CartException e) { return Problem(e.Status, e.Code); }
        catch (DeliveryException e) { return Problem(e.Status, e.Code); }
        catch (PaymentException e) { return Problem(e.Status, e.Code); }
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
