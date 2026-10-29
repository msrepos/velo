using Microsoft.EntityFrameworkCore;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Delivery.Domain;
using Velo.Modules.Inventory.Contracts;

namespace Velo.Modules.Delivery.Infrastructure;

public sealed partial class DeliveryService
{
    public Task<MobileWarehousePage> MinivansAsync(int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        return vans.ListAsync(page, pageSize, ct);
    }

    public async Task<CrewResponse> CreateCrewAsync(CreateCrewRequest request, CancellationToken ct)
    {
        var name = Name(request.Name);
        if (!await users.ExistsAsync(request.UserId, ct)) throw new DeliveryException(400, "verified_crew_user_required");
        return await LockedAsync(["crew-user:" + request.UserId.ToString("N")], async () =>
        {
            if (await db.CrewMembers.AnyAsync(x => x.UserId == request.UserId, ct)) throw new DeliveryException(409, "crew_user_exists");
            var member = new CrewMember { Id = Guid.NewGuid(), UserId = request.UserId, Name = name };
            db.CrewMembers.Add(member); await db.SaveChangesAsync(ct); return MapCrew(member);
        }, ct);
    }

    public Task<CrewResponse> UpdateCrewAsync(Guid id, UpdateCrewRequest request, CancellationToken ct)
    {
        var name = Name(request.Name);
        if (request.Version?.Length != 8) throw new DeliveryException(400, "invalid_version");
        return LockedAsync(["crew:" + id.ToString("N")], async () =>
        {
            var member = await db.CrewMembers.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new DeliveryException(404, "crew_not_found");
            if (!member.RowVersion.SequenceEqual(request.Version!)) throw new DeliveryException(409, "crew_version_conflict");
            if (!request.IsActive && await db.VanDispatches.AnyAsync(x => x.CrewMemberId == id && x.EndedAt == null, ct))
                throw new DeliveryException(409, "crew_has_active_dispatch");
            member.Name = name; member.IsActive = request.IsActive;
            db.Entry(member).Property(x => x.Name).IsModified = true;
            await db.SaveChangesAsync(ct); return MapCrew(member);
        }, ct);
    }

    public async Task<CrewPage> CrewAsync(int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        var query = db.CrewMembers.AsNoTracking();
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(MapCrew).ToArray(), page, pageSize, await query.CountAsync(ct));
    }

    public async Task<VanDispatchPage> VanDispatchesAsync(bool activeOnly, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        var query = db.VanDispatches.AsNoTracking().Where(x => !activeOnly || x.EndedAt == null);
        var count = await query.CountAsync(ct);
        var rows = await query.Include(x => x.Driver).Include(x => x.CrewMember)
            .OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(MapDispatch).ToArray(), page, pageSize, count);
    }

    public async Task<VanDispatchResponse> StartVanDispatchAsync(Guid actor, CreateVanDispatchRequest request, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty) throw new DeliveryException(400, "invalid_operation_id");
        var driverUser = await db.Drivers.AsNoTracking().Where(x => x.Id == request.DriverId).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct)
            ?? throw new DeliveryException(409, "driver_unavailable");
        var crewUser = await db.CrewMembers.AsNoTracking().Where(x => x.Id == request.CrewMemberId).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct)
            ?? throw new DeliveryException(409, "crew_unavailable");
        if (driverUser == crewUser) throw new DeliveryException(400, "different_team_members_required");
        return await LockedAsync(["dispatch:" + request.OperationId.ToString("N"), "van:" + request.WarehouseId.ToString("N"),
            "driver:" + request.DriverId.ToString("N"), "crew:" + request.CrewMemberId.ToString("N"),
            "fleet-user:" + driverUser.ToString("N"), "fleet-user:" + crewUser.ToString("N")], async () =>
        {
            var existing = await DispatchQuery().SingleOrDefaultAsync(x => x.Id == request.OperationId, ct);
            if (existing is not null)
            {
                if (existing.StartedBy != actor || existing.WarehouseId != request.WarehouseId ||
                    existing.DriverId != request.DriverId || existing.CrewMemberId != request.CrewMemberId)
                    throw new DeliveryException(409, "dispatch_operation_conflict");
                return MapDispatch(existing);
            }
            var van = await vans.FindAsync(request.WarehouseId, ct);
            if (van is null || !van.IsActive) throw new DeliveryException(409, "active_mobile_warehouse_required");
            var driver = await db.Drivers.SingleAsync(x => x.Id == request.DriverId, ct);
            var crew = await db.CrewMembers.SingleAsync(x => x.Id == request.CrewMemberId, ct);
            if (!driver.IsActive || !await users.ExistsAsync(driver.UserId, ct)) throw new DeliveryException(409, "driver_unavailable");
            if (!crew.IsActive || !await users.ExistsAsync(crew.UserId, ct)) throw new DeliveryException(409, "crew_unavailable");
            if (await db.VanDispatches.AnyAsync(x => x.EndedAt == null && (x.WarehouseId == request.WarehouseId ||
                x.Driver.UserId == driverUser || x.Driver.UserId == crewUser ||
                x.CrewMember.UserId == driverUser || x.CrewMember.UserId == crewUser), ct))
                throw new DeliveryException(409, "dispatch_resource_busy");
            if (await db.Shipments.AnyAsync(x => x.DriverId == request.DriverId &&
                (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct))
                throw new DeliveryException(409, "driver_has_active_delivery");
            var dispatch = new VanDispatch { Id = request.OperationId, WarehouseId = van.Id, VanCode = van.Code, VanName = van.Name,
                DriverId = driver.Id, Driver = driver, CrewMemberId = crew.Id, CrewMember = crew, StartedBy = actor, StartedAt = time.GetUtcNow() };
            db.VanDispatches.Add(dispatch); await db.SaveChangesAsync(ct); return MapDispatch(dispatch);
        }, ct);
    }

    public async Task<VanDispatchResponse> EndVanDispatchAsync(Guid actor, Guid id, EndVanDispatchRequest request, CancellationToken ct)
    {
        if (request.Version?.Length != 8) throw new DeliveryException(400, "invalid_version");
        var ids = await db.VanDispatches.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.DriverId, x.CrewMemberId }).SingleOrDefaultAsync(ct)
            ?? throw new DeliveryException(404, "dispatch_not_found");
        return await LockedAsync(["dispatch:" + id.ToString("N"), "driver:" + ids.DriverId.ToString("N"), "crew:" + ids.CrewMemberId.ToString("N")], async () =>
        {
            var dispatch = await DispatchQuery().SingleAsync(x => x.Id == id, ct);
            if (!dispatch.RowVersion.SequenceEqual(request.Version!)) throw new DeliveryException(409, "dispatch_version_conflict");
            if (dispatch.EndedAt is not null) return MapDispatch(dispatch);
            if (await db.Shipments.AnyAsync(x => x.DriverId == dispatch.DriverId &&
                (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct))
                throw new DeliveryException(409, "driver_has_active_delivery");
            dispatch.EndedAt = time.GetUtcNow(); dispatch.EndedBy = actor;
            await db.SaveChangesAsync(ct); return MapDispatch(dispatch);
        }, ct);
    }

    // Read the authenticated member's team without exposing the whole fleet.
    public async Task<VanDispatchResponse?> OwnVanDispatchAsync(Guid userId, bool driver, CancellationToken ct)
    {
        if (driver) await OwnDriverIdAsync(userId, ct);
        else if (!await db.CrewMembers.AnyAsync(x => x.UserId == userId && x.IsActive, ct))
            throw new DeliveryException(403, "active_crew_required");
        var row = await DispatchQuery().AsNoTracking().SingleOrDefaultAsync(x => x.EndedAt == null &&
            (driver ? x.Driver.UserId == userId : x.CrewMember.UserId == userId), ct);
        return row is null ? null : MapDispatch(row);
    }

    private IQueryable<VanDispatch> DispatchQuery() => db.VanDispatches.Include(x => x.Driver).Include(x => x.CrewMember);
    private static CrewResponse MapCrew(CrewMember x) => new(x.Id, x.UserId, x.Name, x.IsActive, x.RowVersion);
    private static VanDispatchResponse MapDispatch(VanDispatch x) => new(x.Id, x.WarehouseId, x.VanCode, x.VanName,
        x.DriverId, x.Driver.Name, x.CrewMemberId, x.CrewMember.Name, x.StartedAt, x.StartedBy, x.EndedAt, x.EndedBy, x.RowVersion);
}
