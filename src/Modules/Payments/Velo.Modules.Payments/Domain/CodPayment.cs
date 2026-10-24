namespace Velo.Modules.Payments.Domain;
public sealed class CodPayment
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = "Pending";
    public Guid? DriverId { get; set; }
    public Guid? CollectionOperationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CollectedAt { get; set; }
    public Guid? SettlementId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class CashSettlement
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid ActorUserId { get; set; }
    public decimal Amount { get; set; }
    public required string Reference { get; set; }
    public required string Fingerprint { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
