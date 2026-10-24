using Microsoft.EntityFrameworkCore;
using Velo.Modules.Orders.Domain;

namespace Velo.Modules.Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CustomerOrder> Orders => Set<CustomerOrder>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        builder.Entity<CustomerOrder>(entity =>
        {
            entity.ToTable("Orders", t => t.HasCheckConstraint("CK_Orders_Totals",
                "[Subtotal] > 0 AND [DeliveryFee] >= 0 AND [Total] = [Subtotal] + [DeliveryFee]"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false);
            entity.Property(x => x.PaymentMethod).HasMaxLength(32).IsUnicode(false);
            entity.Property(x => x.PaymentStatus).HasMaxLength(16).IsUnicode(false).HasDefaultValue("Unpaid");
            entity.Property(x => x.RestockDecisionJson).HasMaxLength(2000);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DeliveryFee).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.ReservationId).IsUnique();
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.ExpiresAt });
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines", t => t.HasCheckConstraint("CK_OrderLines_Amounts",
                "[Quantity] BETWEEN 1 AND 1000 AND [UnitPrice] > 0 AND [LineTotal] = [UnitPrice] * [Quantity]"));
            entity.HasKey(x => new { x.OrderId, x.VariantId });
            entity.Property(x => x.ProductName).HasMaxLength(160);
            entity.Property(x => x.VariantName).HasMaxLength(160);
            entity.Property(x => x.Sku).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
        });
        builder.Entity<OrderEvent>(entity =>
        {
            entity.ToTable("OrderEvents");
            entity.HasKey(x => new { x.OrderId, x.Sequence });
            entity.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Reason).HasMaxLength(500);
        });
        builder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.HasKey(x => x.CustomerId);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems", t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] BETWEEN 1 AND 1000"));
            entity.HasKey(x => new { x.CustomerId, x.VariantId });
        });
    }
}
