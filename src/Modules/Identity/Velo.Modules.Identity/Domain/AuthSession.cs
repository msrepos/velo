namespace Velo.Modules.Identity.Domain;

public sealed class AuthSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool IsStaff { get; set; }
    public required string SecurityStamp { get; set; }
    public required string RefreshTokenHash { get; set; }
    public int RefreshVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
