namespace Velo.Modules.Inventory.Contracts;

public sealed record StockItem(Guid VariantId, int Quantity);
public sealed record OrderReservation(Guid Id, string Status, DateTimeOffset ExpiresAt);
public interface IOrderStock
{
    Task<Guid?> FindWarehouseAsync(decimal latitude, decimal longitude, double radiusKm, StockItem[] items, CancellationToken ct);
    Task<OrderReservation> ReserveAsync(Guid operationId, Guid customerId, Guid warehouseId, StockItem[] items, int minutes, CancellationToken ct);
    Task<OrderReservation> FinishAsync(Guid reservationId, bool commit, Guid actor, CancellationToken ct);
    Task RestoreAsync(Guid orderId, Guid warehouseId, StockItem[] items, Guid actor, string reason, CancellationToken ct);
}
public sealed class StockException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
