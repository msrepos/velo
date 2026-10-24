using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Domain;
namespace Velo.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string Schema = "inventory";
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockBalance> Balances => Set<StockBalance>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<InventoryOperation> Operations => Set<InventoryOperation>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();
    public DbSet<ReservationLine> ReservationLines => Set<ReservationLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<Warehouse>(e =>
        {
            e.ToTable("Warehouses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(32).IsUnicode(false);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Type).HasMaxLength(16).IsUnicode(false);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        b.Entity<StockBalance>(e =>
        {
            e.ToTable("StockBalances", t => t.HasCheckConstraint("CK_StockBalances_Quantities",
                "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]"));
            e.HasKey(x => new { x.WarehouseId, x.VariantId });
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        b.Entity<StockMovement>(e =>
        {
            e.ToTable("StockMovements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(32).IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.WarehouseId, x.VariantId, x.CreatedAt });
            e.HasIndex(x => x.OperationId);
            e.HasOne<StockBalance>().WithMany().HasForeignKey(x => new { x.WarehouseId, x.VariantId })
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InventoryOperation>(e =>
        {
            e.ToTable("Operations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
        });
        b.Entity<StockReservation>(e =>
        {
            e.ToTable("Reservations");
            e.Property(x => x.OwnerModule).HasMaxLength(16).IsUnicode(false).HasDefaultValue("Inventory");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(64);
            e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReservationLine>(e =>
        {
            e.ToTable("ReservationLines", t => t.HasCheckConstraint("CK_ReservationLines_Quantity", "[Quantity] > 0"));
            e.HasKey(x => new { x.ReservationId, x.VariantId });
        });
    }
}
