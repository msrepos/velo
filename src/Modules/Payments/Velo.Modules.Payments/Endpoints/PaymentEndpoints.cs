using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Payments.Application;
using Velo.Modules.Payments.Contracts;
using Velo.Modules.Payments.Infrastructure;
namespace Velo.Modules.Payments.Endpoints;
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/payments").WithTags("COD payments")
            .RequireAuthorization(PaymentPolicies.Manage).AddEndpointFilter(Errors);
        admin.MapGet("/", async (PaymentService service, CancellationToken ct, string? status = null, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.ListAsync(status, page, pageSize, ct)));
        admin.MapGet("/{orderId:guid}", async (Guid orderId, PaymentService service, CancellationToken ct) => Results.Ok(await service.GetAsync(orderId, ct)));
        admin.MapPost("/settlements", async (SettlementRequest request, ClaimsPrincipal u, PaymentService service, CancellationToken ct) =>
            Results.Ok(await service.SettleAsync(User(u), request, ct)));
        admin.MapGet("/drivers/{driverId:guid}/cash", async (Guid driverId, PaymentService service, CancellationToken ct) =>
            Results.Ok(await service.CashAsync(driverId, ct)));
        var customer = endpoints.MapGroup("/api/v1/payments").WithTags("My payments").RequireAuthorization().AddEndpointFilter(Errors);
        customer.MapGet("/{orderId:guid}", async (Guid orderId, ClaimsPrincipal u, PaymentService service, CancellationToken ct) =>
            Results.Ok(await service.OwnAsync(User(u), orderId, ct)));
        var driver = endpoints.MapGroup("/api/v1/driver").WithTags("Driver cash").RequireAuthorization().AddEndpointFilter(Errors);
        driver.MapGet("/cash", async (ClaimsPrincipal u, IDeliveries deliveries, PaymentService service, CancellationToken ct) =>
            Results.Ok(await service.CashAsync(await deliveries.OwnDriverIdAsync(User(u), ct), ct)));
        return endpoints;
    }
    private static Guid User(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async ValueTask<object?> Errors(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        c.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return await next(c); }
        catch (PaymentException e) { return Problem(e.Status, e.Code); }
        catch (DeliveryException e) { return Problem(e.Status, e.Code); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "payment_version_conflict"); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) { return Problem(409, "payment_conflict"); }
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
