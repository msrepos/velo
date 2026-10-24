using Microsoft.EntityFrameworkCore;
using Velo.Modules.Notifications.Domain;
namespace Velo.Modules.Notifications.Infrastructure.Persistence;
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";
    public DbSet<InAppNotification> Messages => Set<InAppNotification>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<InAppNotification>(e =>
        {
            e.ToTable("Messages"); e.HasKey(x => x.Id);
            e.Property(x => x.DeduplicationKey).HasMaxLength(160).IsUnicode(false);
            e.HasIndex(x => x.DeduplicationKey).IsUnique();
            e.Property(x => x.Kind).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
    }
}
