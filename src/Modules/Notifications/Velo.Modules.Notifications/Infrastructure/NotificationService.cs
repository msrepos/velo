using Microsoft.EntityFrameworkCore;
using Velo.Modules.Notifications.Contracts;
using Velo.Modules.Notifications.Domain;
using Velo.Modules.Notifications.Infrastructure.Persistence;
namespace Velo.Modules.Notifications.Infrastructure;
public sealed class NotificationService(NotificationsDbContext db, TimeProvider time) : IInAppNotifications
{
    public async Task QueueAsync(Guid userId, string deduplicationKey, string kind, string title, string body, string dataJson, CancellationToken ct)
    {
        if (await db.Messages.AnyAsync(x => x.DeduplicationKey == deduplicationKey, ct)) return;
        db.Messages.Add(new InAppNotification { Id = Guid.NewGuid(), UserId = userId, DeduplicationKey = deduplicationKey,
            Kind = kind, Title = title, Body = body, DataJson = dataJson, CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }
    public async Task<NotificationPage> ListAsync(Guid user, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        var query = db.Messages.AsNoTracking().Where(x => x.UserId == user);
        return new(await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(x => new NotificationResponse(x.Id, x.Kind, x.Title, x.Body, x.DataJson, x.CreatedAt, x.ReadAt)).ToArrayAsync(ct),
            page, pageSize, await query.CountAsync(ct), await query.CountAsync(x => x.ReadAt == null, ct));
    }
    public async Task<bool> ReadAsync(Guid user, Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.Messages.Where(x => x.UserId == user && x.Id == id && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now), ct);
        return await db.Messages.AnyAsync(x => x.Id == id && x.UserId == user, ct);
    }
}
public sealed record NotificationResponse(Guid Id, string Kind, string Title, string Body, string DataJson, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record NotificationPage(NotificationResponse[] Items, int Page, int PageSize, int TotalCount, int UnreadCount);
