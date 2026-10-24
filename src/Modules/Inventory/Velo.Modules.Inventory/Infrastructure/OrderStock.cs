using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Contracts;
using Velo.Modules.Inventory.Infrastructure.Persistence;
namespace Velo.Modules.Inventory.Infrastructure;

public sealed class OrderStock(InventoryService service, InventoryDbContext db) : IOrderStock
{
    public async Task<Guid?> FindWarehouseAsync(decimal latitude, decimal longitude, double radiusKm, StockItem[] items, CancellationToken ct)
    {
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(x => x.IsActive && x.Latitude != null && x.Longitude != null).ToArrayAsync(ct);
        var candidates = warehouses.Select(x => new { x.Id, Distance = Distance(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value) })
            .Where(x => x.Distance <= radiusKm).OrderBy(x => x.Distance).ThenBy(x => x.Id);
        var ids = items.Select(x => x.VariantId).ToArray();
        foreach (var warehouse in candidates)
        {
            await service.ExpireDueAsync(warehouse.Id, null, ct);
            var balances = await db.Balances.AsNoTracking().Where(x => x.WarehouseId == warehouse.Id && ids.Contains(x.VariantId))
                .ToDictionaryAsync(x => x.VariantId, ct);
            if (items.All(x => balances.TryGetValue(x.VariantId, out var balance) && balance.OnHand - balance.Reserved >= x.Quantity))
                return warehouse.Id;
        }
        return null;
    }
    public async Task<OrderReservation> ReserveAsync(Guid operationId, Guid customerId, Guid warehouseId, StockItem[] items, int minutes, CancellationToken ct)
    {
        try
        {
            var result = await service.ReserveAsync(new ReservationRequest(operationId, warehouseId, "order:" + operationId.ToString("N"),
                items.Select(x => new ReservationItem(x.VariantId, x.Quantity)).ToArray(), minutes), customerId, ct, forOrder: true);
            return new(result.Id, result.Status, result.ExpiresAt);
        }
        catch (InventoryException e) { throw new StockException(e.Status, e.Code); }
    }
    public async Task<OrderReservation> FinishAsync(Guid reservationId, bool commit, Guid actor, CancellationToken ct)
    {
        try
        {
            var result = await service.FinishAsync(reservationId, commit, actor, ct, forOrder: true);
            return new(result.Id, result.Status, result.ExpiresAt);
        }
        catch (InventoryException e) { throw new StockException(e.Status, e.Code); }
    }
    public async Task RestoreAsync(Guid orderId, Guid warehouseId, StockItem[] items, Guid actor, string reason, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Order returns require the order transaction coordinator.");
        try
        {
            foreach (var item in items.OrderBy(x => x.VariantId))
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"velo:order-restock:{orderId:N}:{item.VariantId:N}"));
                await service.AdjustAsync(warehouseId, new AdjustmentRequest(new Guid(bytes.AsSpan(0, 16)), item.VariantId, item.Quantity,
                    $"Order {orderId:D} return: {reason}"), actor, ct);
            }
        }
        catch (InventoryException e) { throw new StockException(e.Status, e.Code); }
    }
    private static double Distance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((double)(lat2 - lat1) * rad / 2), 2) +
            Math.Cos((double)lat1 * rad) * Math.Cos((double)lat2 * rad) * Math.Pow(Math.Sin((double)(lon2 - lon1) * rad / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
