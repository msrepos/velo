using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Customers.Application;
using Velo.Modules.Customers.Infrastructure;

namespace Velo.Modules.Customers.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers/me/addresses").WithTags("Customer addresses")
            .RequireAuthorization().AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                try { return await next(context); }
                catch (CustomerException error) { return Problem(error.Status, error.Code); }
                catch (DbUpdateConcurrencyException) { return Problem(409, "address_version_conflict"); }
            });
        group.MapGet("/", async (ClaimsPrincipal user, AddressService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(UserId(user), ct)));
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AddressService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(UserId(user), id, ct)));
        group.MapPost("/", async (AddressRequest request, ClaimsPrincipal user, AddressService service, CancellationToken ct) =>
        {
            var result = await service.SaveAsync(UserId(user), null, request, ct);
            return Results.Created($"/api/v1/customers/me/addresses/{result.Id}", result);
        });
        group.MapPut("/{id:guid}", async (Guid id, AddressRequest request, ClaimsPrincipal user,
            AddressService service, CancellationToken ct) => Results.Ok(await service.SaveAsync(UserId(user), id, request, ct)));
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, AddressService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(UserId(user), id, ct);
            return Results.NoContent();
        });
        return endpoints;
    }
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
