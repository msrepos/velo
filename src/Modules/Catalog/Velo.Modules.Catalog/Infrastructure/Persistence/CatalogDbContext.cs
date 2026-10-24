using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Domain;

namespace Velo.Modules.Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        builder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.NormalizedName).HasMaxLength(160);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.NormalizedName).IsUnique();
        });
        builder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.ImageUrl).HasMaxLength(2048);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CategoryId, x.IsActive });
        });
        builder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariants", table => table.HasCheckConstraint("CK_ProductVariants_Price", "[Price] > 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Barcode).HasMaxLength(64);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            entity.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
