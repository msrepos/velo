namespace Velo.Modules.Inventory.Domain;

public sealed class Warehouse
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class StockBalance
{
    public Guid WarehouseId { get; set; }
    public Guid VariantId { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid VariantId { get; set; }
    public required string Kind { get; set; }
    public int OnHandDelta { get; set; }
    public int ReservedDelta { get; set; }
    public int OnHandAfter { get; set; }
    public int ReservedAfter { get; set; }
    public required string Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class InventoryOperation
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Fingerprint { get; set; }
    public required string ResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class StockReservation
{
    public string OwnerModule { get; set; } = "Inventory";
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public required string Reference { get; set; }
    public required string Status { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public ICollection<ReservationLine> Lines { get; set; } = [];
}
public sealed class ReservationLine
{
    public Guid ReservationId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
}
