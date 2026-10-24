namespace Velo.Modules.Delivery.Domain;
public sealed class Driver
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset? LocationAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class Shipment
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid DriverId { get; set; }
    public Driver Driver { get; set; } = null!;
    public string Status { get; set; } = "Assigned";
    public required string DropoffJson { get; set; }
    public decimal AmountDue { get; set; }
    public string? ProofProtected { get; set; }
    public DateTimeOffset? ProofExpiresAt { get; set; }
    public int ProofAttempts { get; set; }
    public int Attempt { get; set; } = 1;
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
