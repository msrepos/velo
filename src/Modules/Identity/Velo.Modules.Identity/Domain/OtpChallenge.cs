namespace Velo.Modules.Identity.Domain;

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public required string PhoneNumber { get; set; }
    public required string CodeHash { get; set; }
    public required string ClientKey { get; set; }
    public string? RegistrationName { get; set; }
    public string? RegistrationEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    public int Attempts { get; set; }
}
