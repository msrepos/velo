using Microsoft.EntityFrameworkCore;
using Velo.Modules.Customers.Domain;

namespace Velo.Modules.Customers.Infrastructure.Persistence;

public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    public const string Schema = "customers";
    public DbSet<CustomerAddress> Addresses => Set<CustomerAddress>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        builder.Entity<CustomerAddress>(entity =>
        {
            entity.ToTable("Addresses", t => t.HasCheckConstraint("CK_Addresses_Coordinates",
                "[Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Label).HasMaxLength(80);
            entity.Property(x => x.RecipientName).HasMaxLength(160);
            entity.Property(x => x.PhoneNumber).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.Area).HasMaxLength(160);
            entity.Property(x => x.Street).HasMaxLength(300);
            entity.Property(x => x.Building).HasMaxLength(80);
            entity.Property(x => x.Floor).HasMaxLength(40);
            entity.Property(x => x.Apartment).HasMaxLength(40);
            entity.Property(x => x.Instructions).HasMaxLength(500);
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.CustomerId);
            entity.HasIndex(x => new { x.CustomerId, x.IsDefault }).IsUnique().HasFilter("[IsDefault] = 1");
        });
    }
}
