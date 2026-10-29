using Microsoft.EntityFrameworkCore;
using Velo.Modules.Delivery.Domain;
namespace Velo.Modules.Delivery.Infrastructure.Persistence;
public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public const string Schema = "delivery";
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<CrewMember> CrewMembers => Set<CrewMember>();
    public DbSet<VanDispatch> VanDispatches => Set<VanDispatch>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<CrewMember>(e =>
        {
            e.ToTable("CrewMembers"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.RowVersion).IsRowVersion();
        });
        b.Entity<VanDispatch>(e =>
        {
            e.ToTable("VanDispatches"); e.HasKey(x => x.Id);
            e.Property(x => x.VanCode).HasMaxLength(64); e.Property(x => x.VanName).HasMaxLength(160);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Driver).WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CrewMember).WithMany().HasForeignKey(x => x.CrewMemberId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.WarehouseId).IsUnique().HasFilter("[EndedAt] IS NULL");
            e.HasIndex(x => x.DriverId).IsUnique().HasFilter("[EndedAt] IS NULL");
            e.HasIndex(x => x.CrewMemberId).IsUnique().HasFilter("[EndedAt] IS NULL");
        });
        b.Entity<Driver>(e =>
        {
            e.ToTable("Drivers"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.Latitude).HasPrecision(9, 6); e.Property(x => x.Longitude).HasPrecision(9, 6);
        });
        b.Entity<Shipment>(e =>
        {
            e.ToTable("Shipments"); e.HasKey(x => x.OrderId);
            e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.AmountDue).HasPrecision(18, 2);
            e.Property(x => x.ProofProtected).HasMaxLength(2048);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Driver).WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DriverId).IsUnique().HasFilter("[Status] IN ('Assigned', 'OutForDelivery', 'Failed')");
            e.HasIndex(x => new { x.CustomerId, x.AssignedAt });
        });
    }
}
