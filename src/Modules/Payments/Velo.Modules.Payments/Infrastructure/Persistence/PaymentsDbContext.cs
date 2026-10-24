using Microsoft.EntityFrameworkCore;
using Velo.Modules.Payments.Domain;
namespace Velo.Modules.Payments.Infrastructure.Persistence;
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";
    public DbSet<CodPayment> Payments => Set<CodPayment>();
    public DbSet<CashSettlement> Settlements => Set<CashSettlement>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<CodPayment>(e =>
        {
            e.ToTable("CodPayments", t => t.HasCheckConstraint("CK_CodPayments_Amount", "[Amount] > 0"));
            e.HasKey(x => x.OrderId); e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false);
            e.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.CollectionOperationId).IsUnique().HasFilter("[CollectionOperationId] IS NOT NULL");
            e.HasIndex(x => new { x.DriverId, x.SettlementId }); e.HasIndex(x => x.CustomerId);
            e.HasOne<CashSettlement>().WithMany().HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CashSettlement>(e =>
        {
            e.ToTable("CashSettlements", t => t.HasCheckConstraint("CK_CashSettlements_Amount", "[Amount] > 0"));
            e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Reference).HasMaxLength(160); e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
        });
    }
}
