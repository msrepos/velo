using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Contracts;
using Velo.Modules.Catalog.Infrastructure.Persistence;

namespace Velo.Modules.Catalog.Infrastructure;

public sealed class CatalogOfferLookup(CatalogDbContext db) : ICatalogOffers
{
    public async Task<IReadOnlyDictionary<Guid, CatalogOffer>> GetAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        var ids = variantIds.Distinct().ToArray();
        return await db.ProductVariants.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new CatalogOffer(x.Id, x.Product.Name, x.Name, x.Sku, x.Price,
                x.IsActive && x.Product.IsActive && x.Product.Category.IsActive))
            .ToDictionaryAsync(x => x.VariantId, ct);
    }
}
