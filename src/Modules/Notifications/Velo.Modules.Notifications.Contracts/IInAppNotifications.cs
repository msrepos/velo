namespace Velo.Modules.Notifications.Contracts;
public interface IInAppNotifications
{
    Task QueueAsync(Guid userId, string deduplicationKey, string kind, string title, string body, string dataJson, CancellationToken ct);
}
