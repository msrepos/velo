using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Application;
using Velo.Modules.Catalog.Domain;
using Velo.Modules.Catalog.Infrastructure.Persistence;

namespace Velo.Modules.Catalog.Infrastructure;

public sealed class CatalogService(CatalogDbContext db, TimeProvider time)
{
    public async Task<PageResponse<CategoryResponse>> CategoriesAsync(bool admin, int page, int pageSize, CancellationToken ct)
    {
        CatalogValidation.Pagination(page, pageSize);
        var query = db.Categories.AsNoTracking().Where(x => admin || x.IsActive);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CategoryResponse(x.Id, x.Name, x.IsActive, x.RowVersion)).ToArrayAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<CategoryResponse> CategoryAsync(Guid id, bool admin, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.IsActive), ct)
            ?? throw new CatalogException(404, "category_not_found");
        return Map(category);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(CategoryRequest request, CancellationToken ct)
    {
        var name = CatalogValidation.Name(request.Name);
        var now = time.GetUtcNow();
        var category = new Category
        {
            Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant(),
            IsActive = request.IsActive, CreatedAt = now, UpdatedAt = now
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return Map(category);
    }

    public async Task<CategoryResponse> UpdateCategoryAsync(Guid id, CategoryRequest request, CancellationToken ct)
    {
        var name = CatalogValidation.Name(request.Name);
        var version = CatalogValidation.Version(request.Version);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CatalogException(404, "category_not_found");
        db.Entry(category).Property(x => x.RowVersion).OriginalValue = version;
        category.Name = name;
        category.NormalizedName = name.ToUpperInvariant();
        category.IsActive = request.IsActive;
        category.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Map(category);
    }

    public async Task<PageResponse<ProductResponse>> ProductsAsync(bool admin, Guid? categoryId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        CatalogValidation.Pagination(page, pageSize);
        search = search?.Trim();
        if (search?.Length > 100) throw new CatalogException(400, "invalid_search");
        var query = VisibleProducts(admin);
        if (categoryId is not null) query = query.Where(x => x.CategoryId == categoryId);
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Name.Contains(search));
        var total = await query.CountAsync(ct);
        var products = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(x => x.Variants).ToArrayAsync(ct);
        return new(products.Select(x => Map(x, admin)).ToArray(), page, pageSize, total);
    }

    public async Task<ProductResponse> ProductAsync(Guid id, bool admin, CancellationToken ct)
    {
        var product = await VisibleProducts(admin).Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CatalogException(404, "product_not_found");
        return Map(product, admin);
    }

    public async Task<ProductResponse> CreateProductAsync(ProductRequest request, CancellationToken ct)
    {
        var name = CatalogValidation.Name(request.Name);
        var description = CatalogValidation.Description(request.Description);
        var image = CatalogValidation.ImageUrl(request.ImageUrl);
        if (!await db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct))
            throw new CatalogException(400, "invalid_category");
        var now = time.GetUtcNow();
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = request.CategoryId, Name = name,
            Description = description, ImageUrl = image, IsActive = request.IsActive,
            CreatedAt = now, UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return Map(product, true);
    }

    public async Task<ProductResponse> UpdateProductAsync(Guid id, ProductRequest request, CancellationToken ct)
    {
        var name = CatalogValidation.Name(request.Name);
        var version = CatalogValidation.Version(request.Version);
        var description = CatalogValidation.Description(request.Description);
        var image = CatalogValidation.ImageUrl(request.ImageUrl);
        var product = await db.Products.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CatalogException(404, "product_not_found");
        if (!await db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct))
            throw new CatalogException(400, "invalid_category");
        db.Entry(product).Property(x => x.RowVersion).OriginalValue = version;
        product.Name = name;
        product.CategoryId = request.CategoryId;
        product.Description = description;
        product.ImageUrl = image;
        product.IsActive = request.IsActive;
        product.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Map(product, true);
    }

    public async Task<VariantResponse> SaveVariantAsync(Guid productId, Guid? id, VariantRequest request, CancellationToken ct)
    {
        var sku = CatalogValidation.Sku(request.Sku);
        var name = CatalogValidation.Name(request.Name);
        var price = CatalogValidation.Price(request.Price);
        var barcode = CatalogValidation.Barcode(request.Barcode);
        if (!await db.Products.AnyAsync(x => x.Id == productId, ct))
            throw new CatalogException(404, "product_not_found");
        ProductVariant variant;
        if (id is null)
        {
            variant = new ProductVariant
            {
                Id = Guid.NewGuid(), ProductId = productId, Sku = sku, Name = name,
                CreatedAt = time.GetUtcNow()
            };
            db.ProductVariants.Add(variant);
        }
        else
        {
            var version = CatalogValidation.Version(request.Version);
            variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == id && x.ProductId == productId, ct)
                ?? throw new CatalogException(404, "variant_not_found");
            db.Entry(variant).Property(x => x.RowVersion).OriginalValue = version;
        }
        variant.Sku = sku;
        variant.Name = name;
        variant.Price = price;
        variant.Barcode = barcode;
        variant.IsActive = request.IsActive;
        variant.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Map(variant);
    }

    private IQueryable<Product> VisibleProducts(bool admin) => db.Products.AsNoTracking()
        .Where(x => admin || (x.IsActive && x.Category.IsActive && x.Variants.Any(v => v.IsActive)));

    private static CategoryResponse Map(Category x) => new(x.Id, x.Name, x.IsActive, x.RowVersion);
    private static VariantResponse Map(ProductVariant x) => new(x.Id, x.Sku, x.Name, x.Price, "EGP", x.Barcode, x.IsActive, x.RowVersion);
    private static ProductResponse Map(Product x, bool admin) => new(x.Id, x.CategoryId, x.Name,
        x.Description, x.ImageUrl, x.IsActive, x.RowVersion,
        x.Variants.Where(v => admin || v.IsActive).OrderBy(v => v.Name).ThenBy(v => v.Id).Select(Map).ToArray());
}
