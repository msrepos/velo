using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Application;
namespace Velo.Modules.Inventory.Infrastructure;

public sealed partial class InventoryService
{
    public async Task<InventoryPage<BalanceResponse>> BalancesAsync(Guid warehouse, int page, int size, CancellationToken ct)
    {
        Page(page, size);
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouse, ct)) throw new InventoryException(404, "warehouse_not_found");
        await ExpireDueAsync(warehouse, null, ct);
        var query = db.Balances.AsNoTracking().Where(x => x.WarehouseId == warehouse);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.VariantId).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        return new(items.Select(Map).ToArray(), page, size, count);
    }
    public async Task<BalanceResponse> GetBalanceAsync(Guid warehouse, Guid variant, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouse, ct)) throw new InventoryException(404, "warehouse_not_found");
        if (await catalog.GetAsync(variant, ct) is null) throw new InventoryException(400, "invalid_variant");
        await ExpireDueAsync(warehouse, null, ct);
        var value = await db.Balances.AsNoTracking().SingleOrDefaultAsync(x => x.WarehouseId == warehouse && x.VariantId == variant, ct);
        return value is null ? new(warehouse, variant, 0, 0, 0) : Map(value);
    }
    public async Task<AdjustmentResponse> AdjustAsync(Guid warehouse, AdjustmentRequest request, Guid actor, CancellationToken ct)
    {
        if (request.QuantityDelta == 0 || request.QuantityDelta is < -1_000_000 or > 1_000_000)
            throw new InventoryException(400, "invalid_quantity");
        var reason = Text(request.Reason, 500, "invalid_reason");
        var variant = await catalog.GetAsync(request.VariantId, ct);
        await ExpireDueAsync(warehouse, null, ct);
        return await OperationAsync(request.OperationId, actor,
            new { Kind = "Adjustment", Warehouse = warehouse, request.VariantId, request.QuantityDelta, Reason = reason },
            [WarehouseKey(warehouse), BalanceKey(warehouse, request.VariantId)], async () =>
        {
            await ActiveWarehouseAsync(warehouse, ct);
            if (variant is null) throw new InventoryException(400, "invalid_variant");
            var balance = await BalanceAsync(warehouse, request.VariantId, ct);
            var value = (long)balance.OnHand + request.QuantityDelta;
            if (value < balance.Reserved) throw new InventoryException(409, "insufficient_available_stock");
            if (value > int.MaxValue) throw new InventoryException(409, "stock_capacity_exceeded");
            balance.OnHand = (int)value;
            var move = Movement(balance, request.OperationId, "Adjustment", request.QuantityDelta, 0, reason, actor);
            return new AdjustmentResponse(request.OperationId, move.Id, Map(balance));
        }, ct);
    }
    public async Task<TransferResponse> TransferAsync(TransferRequest request, Guid actor, CancellationToken ct)
    {
        if (request.SourceWarehouseId == request.TargetWarehouseId) throw new InventoryException(400, "same_warehouse");
        if (request.Quantity is < 1 or > 1_000_000) throw new InventoryException(400, "invalid_quantity");
        var reason = Text(request.Reason, 500, "invalid_reason");
        var variant = await catalog.GetAsync(request.VariantId, ct);
        await ExpireDueAsync(request.SourceWarehouseId, null, ct);
        await ExpireDueAsync(request.TargetWarehouseId, null, ct);
        return await OperationAsync(request.OperationId, actor,
            new { Kind = "Transfer", request.SourceWarehouseId, request.TargetWarehouseId,
                request.VariantId, request.Quantity, Reason = reason },
            [WarehouseKey(request.SourceWarehouseId), WarehouseKey(request.TargetWarehouseId),
             BalanceKey(request.SourceWarehouseId, request.VariantId), BalanceKey(request.TargetWarehouseId, request.VariantId)],
            async () =>
        {
            await ActiveWarehouseAsync(request.SourceWarehouseId, ct);
            await ActiveWarehouseAsync(request.TargetWarehouseId, ct);
            if (variant is null) throw new InventoryException(400, "invalid_variant");
            var source = await BalanceAsync(request.SourceWarehouseId, request.VariantId, ct);
            var target = await BalanceAsync(request.TargetWarehouseId, request.VariantId, ct);
            if (source.OnHand - source.Reserved < request.Quantity)
                throw new InventoryException(409, "insufficient_available_stock");
            if ((long)target.OnHand + request.Quantity > int.MaxValue)
                throw new InventoryException(409, "stock_capacity_exceeded");
            source.OnHand -= request.Quantity; target.OnHand += request.Quantity;
            Movement(source, request.OperationId, "TransferOut", -request.Quantity, 0, reason, actor);
            Movement(target, request.OperationId, "TransferIn", request.Quantity, 0, reason, actor);
            return new TransferResponse(request.OperationId, Map(source), Map(target));
        }, ct);
    }
    public async Task<InventoryPage<MovementResponse>> MovementsAsync(Guid warehouse, Guid? variant, int page, int size, CancellationToken ct)
    {
        Page(page, size);
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouse, ct)) throw new InventoryException(404, "warehouse_not_found");
        var query = db.Movements.AsNoTracking().Where(x => x.WarehouseId == warehouse);
        if (variant is not null) query = query.Where(x => x.VariantId == variant);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new MovementResponse(x.Id, x.OperationId, x.WarehouseId, x.VariantId, x.Kind,
                x.OnHandDelta, x.ReservedDelta, x.OnHandAfter, x.ReservedAfter, x.Reason, x.ActorUserId, x.CreatedAt))
            .ToArrayAsync(ct);
        return new(items, page, size, count);
    }
}
