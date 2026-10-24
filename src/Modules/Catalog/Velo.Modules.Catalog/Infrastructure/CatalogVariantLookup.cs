using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Contracts;
using Velo.Modules.Catalog.Infrastructure.Persistence;
namespace Velo.Modules.Catalog.Infrastructure;
public sealed class CatalogVariantLookup(CatalogDbContext db) : ICatalogVariants
{
    public Task<CatalogVariant?> GetAsync(Guid variantId, CancellationToken cancellationToken) =>
        db.ProductVariants.AsNoTracking().Where(x => x.Id == variantId)
            .Select(x => new CatalogVariant(x.Id, x.IsActive && x.Product.IsActive && x.Product.Category.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}
