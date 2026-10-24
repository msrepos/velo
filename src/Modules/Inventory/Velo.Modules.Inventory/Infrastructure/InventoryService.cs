using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Contracts;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Domain;
using Velo.Modules.Inventory.Infrastructure.Persistence;
namespace Velo.Modules.Inventory.Infrastructure;

public sealed partial class InventoryService(InventoryDbContext db, ICatalogVariants catalog,
    InventoryTransaction transactions, TimeProvider time)
{
    private static string WarehouseKey(Guid id) => "warehouse:" + id.ToString("N");
    private static string BalanceKey(Guid warehouse, Guid variant) => $"balance:{warehouse:N}:{variant:N}";
    private static string ReservationKey(Guid id) => "reservation:" + id.ToString("N");
    private static string Text(string? input, int maximum, string error)
    {
        input = input?.Trim();
        if (string.IsNullOrWhiteSpace(input) || input.Length > maximum || input.Any(char.IsControl))
            throw new InventoryException(400, error);
        return input;
    }
    private static void Page(int page, int size)
    {
        if (page is < 1 or > 1_000_000 || size is < 1 or > 100)
            throw new InventoryException(400, "invalid_pagination");
    }
    private async Task<Warehouse> ActiveWarehouseAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InventoryException(404, "warehouse_not_found");
        if (!item.IsActive) throw new InventoryException(409, "warehouse_inactive");
        return item;
    }
    private async Task<StockBalance> BalanceAsync(Guid warehouse, Guid variant, CancellationToken ct)
    {
        var item = await db.Balances.SingleOrDefaultAsync(x => x.WarehouseId == warehouse && x.VariantId == variant, ct);
        if (item is null)
        {
            item = new StockBalance { WarehouseId = warehouse, VariantId = variant };
            db.Balances.Add(item);
        }
        return item;
    }
    private StockMovement Movement(StockBalance balance, Guid operation, string kind, int hand, int reserved,
        string reason, Guid? actor)
    {
        var movement = new StockMovement
        {
            Id = Guid.NewGuid(), OperationId = operation, WarehouseId = balance.WarehouseId,
            VariantId = balance.VariantId, Kind = kind, OnHandDelta = hand, ReservedDelta = reserved,
            OnHandAfter = balance.OnHand, ReservedAfter = balance.Reserved,
            Reason = reason, ActorUserId = actor, CreatedAt = time.GetUtcNow()
        };
        db.Movements.Add(movement);
        return movement;
    }
    private static BalanceResponse Map(StockBalance x) =>
        new(x.WarehouseId, x.VariantId, x.OnHand, x.Reserved, x.OnHand - x.Reserved);
    private static ReservationResponse Map(StockReservation x) => new(x.Id, x.WarehouseId, x.Reference,
        x.Status, x.ExpiresAt, x.Lines.OrderBy(l => l.VariantId).Select(l => new ReservationItem(l.VariantId, l.Quantity)).ToArray());

    private Task<T> OperationAsync<T>(Guid id, Guid actor, object payload, IEnumerable<string> keys,
        Func<Task<T>> action, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new InventoryException(400, "invalid_operation_id");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        return transactions.RunAsync(keys.Append("operation:" + id.ToString("N")), async () =>
        {
            var previous = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (previous is not null)
            {
                if (previous.ActorUserId != actor || previous.Fingerprint != hash)
                    throw new InventoryException(409, "idempotency_conflict");
                return JsonSerializer.Deserialize<T>(previous.ResponseJson)!;
            }
            var result = await action();
            db.Operations.Add(new InventoryOperation
            {
                Id = id, ActorUserId = actor, Fingerprint = hash,
                ResponseJson = JsonSerializer.Serialize(result), CreatedAt = time.GetUtcNow()
            });
            return result;
        }, ct);
    }
}
