using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Delivery.Domain;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Identity.Contracts;
namespace Velo.Modules.Delivery.Infrastructure;
public sealed class DeliveryService(DeliveryDbContext db, IVerifiedUsers users, IDataProtectionProvider protection, TimeProvider time) : IDeliveries
{
    private readonly IDataProtector protector = protection.CreateProtector("Velo.Delivery.ReceiptProof.v1");
    private static bool Active(string state) => state is "Assigned" or "OutForDelivery" or "Failed";
    private static string Name(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 160 || value.Any(char.IsControl)) throw new DeliveryException(400, "invalid_driver_name");
        return value;
    }
    public async Task<DriverResponse> CreateDriverAsync(CreateDriverRequest request, CancellationToken ct)
    {
        var name = Name(request.Name);
        if (!await users.ExistsAsync(request.UserId, ct)) throw new DeliveryException(400, "verified_driver_user_required");
        return await LockedAsync(["user:" + request.UserId.ToString("N")], async () =>
        {
            if (await db.Drivers.AnyAsync(x => x.UserId == request.UserId, ct)) throw new DeliveryException(409, "driver_user_exists");
            var driver = new Driver { Id = Guid.NewGuid(), UserId = request.UserId, Name = name };
            db.Drivers.Add(driver); await db.SaveChangesAsync(ct); return Map(driver);
        }, ct);
    }
    public Task<DriverResponse> UpdateDriverAsync(Guid id, UpdateDriverRequest request, CancellationToken ct)
    {
        var name = Name(request.Name);
        if (request.Version?.Length != 8) throw new DeliveryException(400, "invalid_version");
        return LockedAsync(["driver:" + id.ToString("N")], async () =>
        {
            var driver = await db.Drivers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new DeliveryException(404, "driver_not_found");
            if (!driver.RowVersion.SequenceEqual(request.Version!)) throw new DeliveryException(409, "driver_version_conflict");
            if (!request.IsActive && await db.Shipments.AnyAsync(x => x.DriverId == id &&
                (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct))
                throw new DeliveryException(409, "driver_has_active_delivery");
            driver.Name = name; driver.IsActive = request.IsActive;
            db.Entry(driver).Property(x => x.Name).IsModified = true;
            await db.SaveChangesAsync(ct); return Map(driver);
        }, ct);
    }
    public async Task<DriverPage> DriversAsync(int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        var q = db.Drivers.AsNoTracking();
        return new((await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct)).Select(Map).ToArray(),
            page, pageSize, await q.CountAsync(ct));
    }
    public async Task<Guid> OwnDriverIdAsync(Guid userId, CancellationToken ct) =>
        await db.Drivers.AsNoTracking().Where(x => x.UserId == userId && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
        ?? throw new DeliveryException(403, "active_driver_required");
    public async Task<Assignment> OwnAssignmentAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var driver = await OwnDriverIdAsync(userId, ct);
        var job = await db.Shipments.AsNoTracking().Include(x => x.Driver).SingleOrDefaultAsync(x => x.OrderId == orderId && x.DriverId == driver, ct)
            ?? throw new DeliveryException(404, "delivery_not_found");
        return new(job.OrderId, job.CustomerId, driver, job.Driver.UserId, job.Status);
    }
    public async Task<Assignment> AssignAsync(Guid orderId, Guid customerId, Guid driverId, Dropoff dropoff, decimal amount, CancellationToken ct)
    {
        RequireTransaction();
        var existingDriver = await db.Shipments.AsNoTracking().Where(x => x.OrderId == orderId).Select(x => (Guid?)x.DriverId).SingleOrDefaultAsync(ct);
        return await LockedAsync(new[] { "driver:" + driverId.ToString("N"), "driver:" + (existingDriver ?? driverId).ToString("N"), "proof:" + orderId.ToString("N") }, async () =>
        {
            var driver = await db.Drivers.SingleOrDefaultAsync(x => x.Id == driverId && x.IsActive, ct)
                ?? throw new DeliveryException(409, "driver_unavailable");
            if (!await users.ExistsAsync(driver.UserId, ct)) throw new DeliveryException(409, "driver_unavailable");
            if (await db.Shipments.AnyAsync(x => x.DriverId == driverId && x.OrderId != orderId &&
                (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct))
                throw new DeliveryException(409, "driver_busy");
            var job = await db.Shipments.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
            if (job is null)
            {
                job = new Shipment { OrderId = orderId, CustomerId = customerId, DriverId = driverId, DropoffJson = JsonSerializer.Serialize(dropoff), AmountDue = amount };
                db.Shipments.Add(job);
            }
            else
            {
                if (job.Status is not ("Assigned" or "Failed")) throw new DeliveryException(409, "invalid_delivery_state");
                if (job.Status == "Assigned" && job.DriverId == driverId)
                    return new Assignment(orderId, customerId, driverId, driver.UserId, job.Status, Replayed: true);
                if (job.DriverId != driverId || job.Status == "Failed") job.Attempt++;
                job.DriverId = driverId;
            }
            job.Status = "Assigned"; job.AssignedAt = time.GetUtcNow(); job.DispatchedAt = null;
            job.ProofProtected = null; job.ProofExpiresAt = null; job.ProofAttempts = 0; job.FailureReason = null;
            await db.SaveChangesAsync(ct);
            return new Assignment(orderId, customerId, driverId, driver.UserId, job.Status);
        }, ct);
    }
    public async Task DispatchAsync(Guid orderId, Guid driverId, CancellationToken ct)
    {
        RequireTransaction();
        await LockProofAsync(orderId, ct);
        var job = await JobAsync(orderId, driverId, ct);
        if (job.Status == "OutForDelivery") return;
        if (job.Status != "Assigned") throw new DeliveryException(409, "invalid_delivery_state");
        job.Status = "OutForDelivery"; job.DispatchedAt = time.GetUtcNow(); Rotate(job);
        await db.SaveChangesAsync(ct);
    }
    public async Task<ProofResult> CompleteAsync(Guid orderId, Guid driverId, string proofCode, CancellationToken ct)
    {
        RequireTransaction();
        await LockProofAsync(orderId, ct);
        var job = await JobAsync(orderId, driverId, ct);
        if (job.Status != "OutForDelivery") throw new DeliveryException(409, "invalid_delivery_state");
        if (job.ProofAttempts >= 5) return new(false, "delivery_code_locked", 429);
        if (job.ProofExpiresAt <= time.GetUtcNow()) return new(false, "delivery_code_expired", 409);
        string expected;
        try { expected = protector.Unprotect(job.ProofProtected!); }
        catch (CryptographicException) { throw new DeliveryException(503, "delivery_code_unavailable"); }
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(proofCode)))
        {
            job.ProofAttempts++; await db.SaveChangesAsync(ct);
            return new(false, job.ProofAttempts >= 5 ? "delivery_code_locked" : "invalid_delivery_code", job.ProofAttempts >= 5 ? 429 : 400);
        }
        job.Status = "Delivered"; job.CompletedAt = time.GetUtcNow();
        job.ProofProtected = null; job.ProofExpiresAt = null;
        await db.SaveChangesAsync(ct); return new(true, null, 200);
    }
    public async Task FailAsync(Guid orderId, Guid driverId, string reason, CancellationToken ct)
    {
        RequireTransaction();
        await LockProofAsync(orderId, ct);
        var job = await JobAsync(orderId, driverId, ct);
        if (job.Status == "Failed") return;
        if (job.Status != "OutForDelivery") throw new DeliveryException(409, "invalid_delivery_state");
        job.Status = "Failed"; job.FailureReason = reason; job.ProofProtected = null; job.ProofExpiresAt = null;
        await db.SaveChangesAsync(ct);
    }
    public async Task CloseAsync(Guid orderId, string status, CancellationToken ct)
    {
        RequireTransaction();
        await LockProofAsync(orderId, ct);
        var job = await db.Shipments.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (job is null) return;
        if (status is not ("Cancelled" or "Returned") || job.Status is not ("Assigned" or "Failed"))
            throw new DeliveryException(409, "invalid_delivery_state");
        job.Status = status; job.CompletedAt = time.GetUtcNow(); job.ProofProtected = null; job.ProofExpiresAt = null;
        await db.SaveChangesAsync(ct);
    }
    public async Task<DeliveryCodeResponse> CustomerCodeAsync(Guid customer, Guid orderId, CancellationToken ct) =>
        await LockedAsync(["proof:" + orderId.ToString("N")], async () =>
        {
            var job = await db.Shipments.SingleOrDefaultAsync(x => x.OrderId == orderId && x.CustomerId == customer, ct)
                ?? throw new DeliveryException(404, "delivery_not_found");
            if (job.Status != "OutForDelivery") throw new DeliveryException(409, "delivery_not_in_transit");
            if (job.ProofProtected is null || job.ProofExpiresAt <= time.GetUtcNow() || job.ProofAttempts >= 5)
            { Rotate(job); await db.SaveChangesAsync(ct); }
            return new DeliveryCodeResponse(protector.Unprotect(job.ProofProtected!), job.ProofExpiresAt!.Value);
        }, ct);
    public async Task<TrackingResponse> TrackingAsync(Guid customer, Guid orderId, CancellationToken ct)
    {
        var job = await db.Shipments.AsNoTracking().Include(x => x.Driver).SingleOrDefaultAsync(x => x.OrderId == orderId && x.CustomerId == customer, ct)
            ?? throw new DeliveryException(404, "delivery_not_found");
        var visible = job.Status == "OutForDelivery" && job.Driver.LocationAt >= job.DispatchedAt && job.Driver.LocationAt >= time.GetUtcNow().AddMinutes(-5);
        return new(job.OrderId, job.Status, job.Driver.Name, visible ? job.Driver.Latitude : null,
            visible ? job.Driver.Longitude : null, visible ? job.Driver.LocationAt : null);
    }
    public async Task LocationAsync(Guid userId, LocationRequest request, CancellationToken ct)
    {
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 ||
            decimal.Round(request.Latitude, 6) != request.Latitude || decimal.Round(request.Longitude, 6) != request.Longitude)
            throw new DeliveryException(400, "invalid_coordinates");
        var id = await OwnDriverIdAsync(userId, ct);
        await LockedAsync(["driver:" + id.ToString("N")], async () =>
        {
            var driver = await db.Drivers.SingleAsync(x => x.Id == id, ct);
            if (!driver.IsActive) throw new DeliveryException(403, "active_driver_required");
            if (driver.LocationAt > time.GetUtcNow().AddSeconds(-5)) throw new DeliveryException(429, "location_update_throttled", 5);
            driver.Latitude = request.Latitude; driver.Longitude = request.Longitude; driver.LocationAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct); return true;
        }, ct);
    }
    public async Task<DriverDeliveryPage> OwnJobsAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var driver = await OwnDriverIdAsync(userId, ct);
        var query = db.Shipments.AsNoTracking().Where(x => x.DriverId == driver);
        var rows = await query.OrderByDescending(x => x.AssignedAt).ThenBy(x => x.OrderId).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(x => new DriverDeliveryResponse(x.OrderId, x.Status, Active(x.Status) ? x.AmountDue : 0,
            Active(x.Status) ? JsonSerializer.Deserialize<Dropoff>(x.DropoffJson) : null, x.AssignedAt, x.DispatchedAt, x.FailureReason, x.Attempt)).ToArray(),
            page, pageSize, await query.CountAsync(ct));
    }
    private Task<bool> LockProofAsync(Guid orderId, CancellationToken ct) =>
        ApplyAsync(["proof:" + orderId.ToString("N")], () => Task.FromResult(true), ct);
    private void Rotate(Shipment job)
    {
        job.ProofProtected = protector.Protect(RandomNumberGenerator.GetInt32(1_000_000).ToString("D6"));
        job.ProofExpiresAt = time.GetUtcNow().AddHours(1); job.ProofAttempts = 0;
    }
    private async Task<Shipment> JobAsync(Guid order, Guid driver, CancellationToken ct) =>
        await db.Shipments.SingleOrDefaultAsync(x => x.OrderId == order && x.DriverId == driver, ct) ?? throw new DeliveryException(404, "delivery_not_found");
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Delivery lifecycle changes require the order transaction coordinator.");
    }
    private Task<T> LockedAsync<T>(IEnumerable<string> keys, Func<Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return ApplyAsync(keys, action, ct);
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await ApplyAsync(keys, action, ct); await transaction.CommitAsync(ct); return result;
        });
    }
    private async Task<T> ApplyAsync<T>(IEnumerable<string> keys, Func<Task<T>> action, CancellationToken ct)
    {
        foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
        {
            var resource = "velo:delivery:" + key;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Delivery lock unavailable.', 1;
                """, ct);
        }
        return await action();
    }
    private static DriverResponse Map(Driver d) => new(d.Id, d.UserId, d.Name, d.IsActive, d.RowVersion);
    private static void Page(int page, int size)
    { if (page is < 1 or > 1_000_000 || size is < 1 or > 100) throw new DeliveryException(400, "invalid_pagination"); }
}
