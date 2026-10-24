using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Domain;
namespace Velo.Modules.Inventory.Infrastructure;

public sealed partial class InventoryService
{
    public async Task<ReservationResponse> ReserveAsync(ReservationRequest request, Guid actor, CancellationToken ct, bool forOrder = false)
    {
        var reference = Text(request.Reference, 64, "invalid_reference");
        if (request.Items is null || request.Items.Length is < 1 or > 100 ||
            request.Items.Any(x => x is null || x.VariantId == Guid.Empty || x.Quantity is < 1 or > 1_000_000) ||
            request.Items.Select(x => x.VariantId).Distinct().Count() != request.Items.Length ||
            request.LifetimeMinutes is < 1 or > 30)
            throw new InventoryException(400, "invalid_reservation");
        var items = request.Items.OrderBy(x => x.VariantId).ToArray();
        var known = new Dictionary<Guid, bool>();
        foreach (var item in items)
            known[item.VariantId] = (await catalog.GetAsync(item.VariantId, ct))?.IsPublished == true;
        await ExpireDueAsync(request.WarehouseId, null, ct);
        var keys = items.Select(x => BalanceKey(request.WarehouseId, x.VariantId))
            .Append(WarehouseKey(request.WarehouseId));
        return await OperationAsync(request.OperationId, actor,
            new { Kind = forOrder ? "OrderReservation" : "Reservation", request.WarehouseId, Reference = reference, Items = items, request.LifetimeMinutes },
            keys, async () =>
        {
            await ActiveWarehouseAsync(request.WarehouseId, ct);
            if (known.Values.Any(x => !x)) throw new InventoryException(400, "variant_unavailable");
            if (await db.Reservations.AnyAsync(x => x.Reference == reference, ct))
                throw new InventoryException(409, "reservation_reference_exists");
            var now = time.GetUtcNow();
            var reservation = new StockReservation
            {
                Id = request.OperationId, WarehouseId = request.WarehouseId, Reference = reference, Status = "Pending",
                OwnerModule = forOrder ? "Orders" : "Inventory",
                CreatedBy = actor, CreatedAt = now, ExpiresAt = now.AddMinutes(request.LifetimeMinutes),
                Lines = items.Select(x => new ReservationLine { ReservationId = request.OperationId,
                    VariantId = x.VariantId, Quantity = x.Quantity }).ToArray()
            };
            foreach (var item in items)
            {
                var balance = await BalanceAsync(request.WarehouseId, item.VariantId, ct);
                if (balance.OnHand - balance.Reserved < item.Quantity)
                    throw new InventoryException(409, "insufficient_available_stock");
                balance.Reserved += item.Quantity;
                Movement(balance, request.OperationId, "Reserve", 0, item.Quantity, reference, actor);
            }
            db.Reservations.Add(reservation);
            return Map(reservation);
        }, ct);
    }

    public async Task<ReservationResponse> ReservationAsync(Guid id, CancellationToken ct)
    {
        await ExpireAsync(id, ct);
        return Map(await db.Reservations.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InventoryException(404, "reservation_not_found"));
    }

    public async Task<ReservationResponse> FinishAsync(Guid id, bool commit, Guid actor, CancellationToken ct, bool forOrder = false)
    {
        await ExpireAsync(id, ct);
        var snapshot = await db.Reservations.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InventoryException(404, "reservation_not_found");
        if (snapshot.OwnerModule == "Orders" && !forOrder)
            throw new InventoryException(409, "order_managed_reservation");
        return await transactions.RunAsync(ReservationKeys(snapshot), async () =>
        {
            var item = await db.Reservations.Include(x => x.Lines).SingleAsync(x => x.Id == id, ct);
            var target = commit ? "Committed" : "Released";
            if (item.Status == target || (!commit && item.Status == "Expired")) return Map(item);
            if (item.Status != "Pending") throw new InventoryException(409, "invalid_reservation_state");
            // Time may have advanced while waiting for the transaction lock.
            if (item.ExpiresAt <= time.GetUtcNow()) throw new InventoryException(409, "reservation_expired");
            await CompleteAsync(item, target, actor, ct);
            return Map(item);
        }, ct);
    }

    public async Task ExpireDueAsync(Guid? warehouse, int? limit, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var query = db.Reservations.AsNoTracking().Where(x => x.Status == "Pending" && x.ExpiresAt <= now);
        if (warehouse is not null) query = query.Where(x => x.WarehouseId == warehouse);
        query = query.OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id);
        if (limit is { } count) query = query.Take(count);
        foreach (var id in await query.Select(x => x.Id).ToArrayAsync(ct))
            await ExpireAsync(id, ct);
    }

    private async Task ExpireAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await db.Reservations.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id && x.Status == "Pending" && x.ExpiresAt <= time.GetUtcNow(), ct);
        if (snapshot is null) return;
        await transactions.RunAsync(ReservationKeys(snapshot), async () =>
        {
            var item = await db.Reservations.Include(x => x.Lines).SingleAsync(x => x.Id == id, ct);
            if (item.Status == "Pending" && item.ExpiresAt <= time.GetUtcNow())
                await CompleteAsync(item, "Expired", null, ct);
            return true;
        }, ct);
    }

    private IEnumerable<string> ReservationKeys(StockReservation item) =>
        item.Lines.Select(x => BalanceKey(item.WarehouseId, x.VariantId)).Append(ReservationKey(item.Id));

    private async Task CompleteAsync(StockReservation item, string status, Guid? actor, CancellationToken ct)
    {
        foreach (var line in item.Lines)
        {
            var balance = await db.Balances.SingleAsync(x => x.WarehouseId == item.WarehouseId && x.VariantId == line.VariantId, ct);
            if (balance.Reserved < line.Quantity) throw new InventoryException(409, "inventory_inconsistent");
            balance.Reserved -= line.Quantity;
            var hand = status == "Committed" ? -line.Quantity : 0;
            balance.OnHand += hand;
            Movement(balance, item.Id, status == "Committed" ? "Commit" : status == "Released" ? "Release" : "Expire",
                hand, -line.Quantity, item.Reference, actor);
        }
        item.Status = status;
        item.CompletedAt = time.GetUtcNow();
    }
}
