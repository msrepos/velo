namespace Velo.Modules.Notifications.Domain;
public sealed class InAppNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string DeduplicationKey { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string DataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
