using Microsoft.EntityFrameworkCore;
using Velo.Modules.Delivery.Contracts;

namespace Velo.Modules.Delivery.Infrastructure;

public sealed partial class DeliveryService
{
    public async Task<Assignment?> OwnActiveAssignmentAsync(Guid userId, CancellationToken ct)
    {
        var driver = await OwnDriverIdAsync(userId, ct);
        var job = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.DriverId == driver &&
            (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct);
        return job is null ? null : new(job.OrderId, job.CustomerId, driver, userId, job.Status);
    }

    public async Task<DriverVan?> OwnVanAsync(Guid userId, CancellationToken ct)
    {
        var id = await OwnDriverIdAsync(userId, ct);
        return await db.VanDispatches.AsNoTracking().Where(x => x.DriverId == id && x.EndedAt == null)
            .Select(x => new DriverVan(x.WarehouseId, id)).SingleOrDefaultAsync(ct);
    }

    public async Task<DriverJourney> OwnJourneyAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var driver = await OwnDriverIdAsync(userId, ct);
        var job = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId && x.DriverId == driver, ct)
            ?? throw new DeliveryException(404, "delivery_not_found");
        return new(job.OrderId, job.Status, job.TravelStartedAt, job.ArrivedAt);
    }

    public async Task<Assignment?> TryAssignVanAsync(Guid orderId, Guid customerId, Guid warehouseId, Dropoff dropoff, decimal amount, CancellationToken ct)
    {
        RequireTransaction();
        var shift = await db.VanDispatches.AsNoTracking().Where(x => x.WarehouseId == warehouseId && x.EndedAt == null)
            .Select(x => new { x.Id, x.DriverId }).SingleOrDefaultAsync(ct);
        if (shift is null) return null;
        return await LockedAsync<Assignment?>(["driver:" + shift.DriverId.ToString("N"), "proof:" + orderId.ToString("N")], async () =>
        {
            if (!await db.VanDispatches.AnyAsync(x => x.Id == shift.Id && x.EndedAt == null, ct)) return null;
            var driver = await db.Drivers.AsNoTracking().SingleAsync(x => x.Id == shift.DriverId, ct);
            if (!driver.IsActive || !await users.ExistsAsync(driver.UserId, ct)) return null;
            var existing = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
            if (existing is not null)
                return existing.DriverId == driver.Id && existing.Status == "Assigned"
                    ? new Assignment(orderId, customerId, driver.Id, driver.UserId, "Assigned", Replayed: true) : null;
            if (await db.Shipments.AnyAsync(x => x.DriverId == driver.Id &&
                (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct)) return null;
            return await AssignAsync(orderId, customerId, driver.Id, dropoff, amount, ct);
        }, ct);
    }

    public async Task<DriverJourney> TravelAsync(Guid userId, Guid orderId, bool arrived, CancellationToken ct)
    {
        var driver = await OwnDriverIdAsync(userId, ct);
        return await LockedAsync(["driver:" + driver.ToString("N"), "proof:" + orderId.ToString("N")], async () =>
        {
            var job = await JobAsync(orderId, driver, ct);
            if (job.Status is not ("Assigned" or "OutForDelivery")) throw new DeliveryException(409, "invalid_delivery_state");
            if (arrived)
            {
                if (job.TravelStartedAt is null) throw new DeliveryException(409, "travel_not_started");
                job.ArrivedAt ??= time.GetUtcNow();
            }
            else job.TravelStartedAt ??= time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new DriverJourney(orderId, job.Status, job.TravelStartedAt, job.ArrivedAt);
        }, ct);
    }
}
