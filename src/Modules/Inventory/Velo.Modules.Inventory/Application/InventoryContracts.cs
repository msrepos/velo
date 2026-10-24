namespace Velo.Modules.Inventory.Application;

public sealed record WarehouseRequest(string Code, string Name, string Type = "Fixed", string? Address = null,
    decimal? Latitude = null, decimal? Longitude = null, bool IsActive = true, byte[]? Version = null);
public sealed record WarehouseResponse(Guid Id, string Code, string Name, string Type, string? Address,
    decimal? Latitude, decimal? Longitude, bool IsActive, byte[] Version);
public sealed record BalanceResponse(Guid WarehouseId, Guid VariantId, int OnHand, int Reserved, int Available);
public sealed record AdjustmentRequest(Guid OperationId, Guid VariantId, int QuantityDelta, string Reason);
public sealed record AdjustmentResponse(Guid OperationId, Guid MovementId, BalanceResponse Balance);
public sealed record TransferRequest(Guid OperationId, Guid SourceWarehouseId, Guid TargetWarehouseId,
    Guid VariantId, int Quantity, string Reason);
public sealed record TransferResponse(Guid OperationId, BalanceResponse Source, BalanceResponse Target);
public sealed record ReservationItem(Guid VariantId, int Quantity);
public sealed record ReservationRequest(Guid OperationId, Guid WarehouseId, string Reference,
    ReservationItem[] Items, int LifetimeMinutes = 10);
public sealed record ReservationResponse(Guid Id, Guid WarehouseId, string Reference, string Status,
    DateTimeOffset ExpiresAt, ReservationItem[] Items);
public sealed record MovementResponse(Guid Id, Guid OperationId, Guid WarehouseId, Guid VariantId,
    string Kind, int OnHandDelta, int ReservedDelta, int OnHandAfter, int ReservedAfter,
    string Reason, Guid? ActorUserId, DateTimeOffset CreatedAt);
public sealed record InventoryPage<T>(T[] Items, int Page, int PageSize, int TotalCount);
public sealed class InventoryException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public static class InventoryPolicies
{
    public const string Manage = "Inventory.Manage";
    public const string Permission = "inventory.manage";
}
public sealed class InventoryOptions
{
    public bool EnableExpiryWorker { get; set; } = true;
}
