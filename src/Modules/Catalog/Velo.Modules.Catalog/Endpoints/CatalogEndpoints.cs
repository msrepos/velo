using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Application;
using Velo.Modules.Catalog.Infrastructure;

namespace Velo.Modules.Catalog.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicApi = endpoints.MapGroup("/api/v1/catalog").WithTags("Catalog").AllowAnonymous();
        var adminApi = endpoints.MapGroup("/api/v1/admin/catalog").WithTags("Catalog administration")
            .RequireAuthorization(CatalogPolicies.Manage);
        foreach (var (group, admin) in new[] { (publicApi, false), (adminApi, true) })
        {
            group.AddEndpointFilter(HandleErrors);
            group.MapGet("/categories", async (CatalogService service, CancellationToken ct,
                int page = 1, int pageSize = 20) => Results.Ok(await service.CategoriesAsync(admin, page, pageSize, ct)));
            group.MapGet("/categories/{id:guid}", async (Guid id, CatalogService service, CancellationToken ct) =>
                Results.Ok(await service.CategoryAsync(id, admin, ct)));
            group.MapGet("/products", async (CatalogService service, CancellationToken ct,
                Guid? categoryId = null, string? search = null, int page = 1, int pageSize = 20) =>
                Results.Ok(await service.ProductsAsync(admin, categoryId, search, page, pageSize, ct)));
            group.MapGet("/products/{id:guid}", async (Guid id, CatalogService service, CancellationToken ct) =>
                Results.Ok(await service.ProductAsync(id, admin, ct)));
        }

        adminApi.MapPost("/categories", async (CategoryRequest request, CatalogService service, CancellationToken ct) =>
        {
            var result = await service.CreateCategoryAsync(request, ct);
            return Results.Created($"/api/v1/admin/catalog/categories/{result.Id}", result);
        });
        adminApi.MapPut("/categories/{id:guid}", async (Guid id, CategoryRequest request, CatalogService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateCategoryAsync(id, request, ct)));
        adminApi.MapPost("/products", async (ProductRequest request, CatalogService service, CancellationToken ct) =>
        {
            var result = await service.CreateProductAsync(request, ct);
            return Results.Created($"/api/v1/admin/catalog/products/{result.Id}", result);
        });
        adminApi.MapPut("/products/{id:guid}", async (Guid id, ProductRequest request, CatalogService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateProductAsync(id, request, ct)));
        adminApi.MapPost("/products/{productId:guid}/variants", async (Guid productId, VariantRequest request,
            CatalogService service, CancellationToken ct) =>
        {
            var result = await service.SaveVariantAsync(productId, null, request, ct);
            return Results.Created($"/api/v1/admin/catalog/products/{productId}", result);
        });
        adminApi.MapPut("/products/{productId:guid}/variants/{id:guid}", async (Guid productId, Guid id,
            VariantRequest request, CatalogService service, CancellationToken ct) =>
                Results.Ok(await service.SaveVariantAsync(productId, id, request, ct)));
        return endpoints;
    }

    private static async ValueTask<object?> HandleErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (CatalogException error) { return Problem(error.StatusCode, error.Code); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "catalog_version_conflict"); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Problem(409, "duplicate_catalog_value");
        }
    }

    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
