using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Infrastructure;

namespace Velo.Modules.Orders.Endpoints;

public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cart").WithTags("Cart").RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                try { return await next(context); }
                catch (CartException error) { return Results.Problem(statusCode: error.Status, title: error.Code,
                    extensions: new Dictionary<string, object?> { ["code"] = error.Code }); }
            });
        group.MapGet("/", async (ClaimsPrincipal user, HttpContext http, CartService service, CancellationToken ct) =>
            Reply(http, await service.GetAsync(UserId(user), ct)));
        group.MapPut("/items/{variantId:guid}", async (Guid variantId, SetCartItemRequest request,
            ClaimsPrincipal user, HttpContext http, CartService service, CancellationToken ct) =>
                Reply(http, await service.SetItemAsync(UserId(user), variantId, request, http.Request.Headers.IfMatch, ct)));
        group.MapDelete("/items/{variantId:guid}", async (Guid variantId, ClaimsPrincipal user,
            HttpContext http, CartService service, CancellationToken ct) =>
                Reply(http, await service.RemoveItemAsync(UserId(user), variantId, http.Request.Headers.IfMatch, ct)));
        group.MapDelete("/", async (ClaimsPrincipal user, HttpContext http, CartService service, CancellationToken ct) =>
            Reply(http, await service.ClearAsync(UserId(user), http.Request.Headers.IfMatch, ct)));
        return endpoints;
    }
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Reply(HttpContext http, CartResponse response)
    {
        http.Response.Headers.ETag = '"' + response.Version + '"';
        return Results.Ok(response);
    }
}
