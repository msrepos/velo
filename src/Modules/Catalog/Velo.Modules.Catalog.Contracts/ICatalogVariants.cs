namespace Velo.Modules.Catalog.Contracts;
public sealed record CatalogVariant(Guid Id, bool IsPublished);
public interface ICatalogVariants
{
    Task<CatalogVariant?> GetAsync(Guid variantId, CancellationToken cancellationToken);
}
