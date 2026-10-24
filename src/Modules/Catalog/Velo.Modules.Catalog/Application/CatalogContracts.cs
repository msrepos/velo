namespace Velo.Modules.Catalog.Application;

public sealed record CategoryRequest(string Name, bool IsActive = true, byte[]? Version = null);
public sealed record ProductRequest(Guid CategoryId, string Name, string? Description = null,
    string? ImageUrl = null, bool IsActive = true, byte[]? Version = null);
public sealed record VariantRequest(string Sku, string Name, decimal Price, string? Barcode = null,
    bool IsActive = true, byte[]? Version = null);

public sealed record CategoryResponse(Guid Id, string Name, bool IsActive, byte[] Version);
public sealed record VariantResponse(Guid Id, string Sku, string Name, decimal Price,
    string Currency, string? Barcode, bool IsActive, byte[] Version);
public sealed record ProductResponse(Guid Id, Guid CategoryId, string Name, string? Description,
    string? ImageUrl, bool IsActive, byte[] Version, VariantResponse[] Variants);
public sealed record PageResponse<T>(T[] Items, int Page, int PageSize, int TotalCount);

public sealed class CatalogException(int statusCode, string code) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public static class CatalogPolicies
{
    public const string Manage = "Catalog.Manage";
    public const string Permission = "catalog.manage";
}
