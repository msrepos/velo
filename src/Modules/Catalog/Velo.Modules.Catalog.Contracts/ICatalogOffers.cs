namespace Velo.Modules.Catalog.Contracts;

public sealed record CatalogOffer(Guid VariantId, string ProductName, string VariantName,
    string Sku, decimal Price, bool IsPublished);

public interface ICatalogOffers
{
    Task<IReadOnlyDictionary<Guid, CatalogOffer>> GetAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct);
}
