using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Payments.Application;
using Velo.Modules.Payments.Contracts;
using Velo.Modules.Payments.Domain;
using Velo.Modules.Payments.Infrastructure.Persistence;
namespace Velo.Modules.Payments.Infrastructure;
public sealed class PaymentService(PaymentsDbContext db, TimeProvider time) : ICodPayments
{
    public async Task CreateAsync(Guid orderId, Guid customerId, decimal amount, CancellationToken ct)
    {
        RequireTransaction();
        if (amount <= 0 || amount > 1_000_000m || decimal.Round(amount, 2) != amount) throw new PaymentException(400, "invalid_payment_amount");
        var existing = await db.Payments.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (existing is not null)
        {
            if (existing.Amount != amount || existing.CustomerId != customerId) throw new PaymentException(409, "payment_identity_conflict");
            return;
        }
        db.Payments.Add(new CodPayment { OrderId = orderId, CustomerId = customerId, Amount = amount, CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }
    public async Task CancelAsync(Guid orderId, CancellationToken ct)
    {
        RequireTransaction();
        var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId, ct);
        if (payment.Status == "Cancelled") return;
        if (payment.Status != "Pending") throw new PaymentException(409, "payment_already_collected");
        payment.Status = "Cancelled"; await db.SaveChangesAsync(ct);
    }
    public async Task<PaymentReceipt> CollectAsync(Guid orderId, Guid driverId, Guid operationId, decimal amount, CancellationToken ct)
    {
        RequireTransaction();
        if (operationId == Guid.Empty) throw new PaymentException(400, "invalid_operation_id");
        await LocksAsync(["driver:" + driverId.ToString("N"), "operation:" + operationId.ToString("N")], ct);
        var payment = await db.Payments.SingleAsync(x => x.OrderId == orderId, ct);
        if (payment.Status == "Collected")
        {
            if (payment.CollectionOperationId != operationId || payment.DriverId != driverId || payment.Amount != amount)
                throw new PaymentException(409, "collection_idempotency_conflict");
            return Map(payment);
        }
        if (payment.Status != "Pending" || payment.Amount != amount) throw new PaymentException(409, "payment_amount_or_state_conflict");
        if (await db.Payments.AnyAsync(x => x.CollectionOperationId == operationId, ct) || await db.Settlements.AnyAsync(x => x.Id == operationId, ct))
            throw new PaymentException(409, "payment_operation_exists");
        payment.Status = "Collected"; payment.DriverId = driverId; payment.CollectionOperationId = operationId;
        payment.CollectedAt = time.GetUtcNow(); await db.SaveChangesAsync(ct); return Map(payment);
    }
    public async Task<PaymentReceipt> GetAsync(Guid orderId, CancellationToken ct) => Map(await db.Payments.AsNoTracking()
        .SingleOrDefaultAsync(x => x.OrderId == orderId, ct) ?? throw new PaymentException(404, "payment_not_found"));
    public async Task<PaymentReceipt> OwnAsync(Guid customer, Guid orderId, CancellationToken ct) => Map(await db.Payments.AsNoTracking()
        .SingleOrDefaultAsync(x => x.OrderId == orderId && x.CustomerId == customer, ct) ?? throw new PaymentException(404, "payment_not_found"));
    public async Task<CashBalanceResponse> CashAsync(Guid driver, CancellationToken ct)
    {
        var query = db.Payments.AsNoTracking().Where(x => x.DriverId == driver && x.Status == "Collected" && x.SettlementId == null);
        return new(driver, await query.SumAsync(x => (decimal?)x.Amount, ct) ?? 0, "EGP", await query.CountAsync(ct));
    }
    public async Task<PaymentPage> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new PaymentException(400, "invalid_pagination");
        if (status is not (null or "Pending" or "Collected" or "Cancelled")) throw new PaymentException(400, "invalid_payment_status");
        var query = db.Payments.AsNoTracking(); if (status is not null) query = query.Where(x => x.Status == status);
        return new((await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.OrderId).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct))
            .Select(Map).ToArray(), page, pageSize, await query.CountAsync(ct));
    }
    public Task<SettlementResponse> SettleAsync(Guid actor, SettlementRequest request, CancellationToken ct)
    {
        var reference = request.Reference?.Trim();
        if (request.OperationId == Guid.Empty || request.DriverId == Guid.Empty || string.IsNullOrEmpty(reference) || reference.Length > 160 || reference.Any(char.IsControl) ||
            request.OrderIds is null || request.OrderIds.Length is < 1 or > 100 || request.OrderIds.Contains(Guid.Empty) ||
            request.OrderIds.Distinct().Count() != request.OrderIds.Length || request.AmountReceived <= 0 || decimal.Round(request.AmountReceived, 2) != request.AmountReceived)
            throw new PaymentException(400, "invalid_settlement");
        var ids = request.OrderIds.Order().ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.DriverId, OrderIds = ids, Amount = request.AmountReceived, Reference = reference })));
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LocksAsync(["driver:" + request.DriverId.ToString("N"), "operation:" + request.OperationId.ToString("N")], ct);
            var previous = await db.Settlements.SingleOrDefaultAsync(x => x.Id == request.OperationId, ct);
            if (previous is not null)
            {
                if (previous.ActorUserId != actor || previous.Fingerprint != hash) throw new PaymentException(409, "settlement_idempotency_conflict");
                await transaction.CommitAsync(ct); return new SettlementResponse(previous.Id, previous.DriverId, previous.Amount, previous.Reference, ids, previous.CreatedAt);
            }
            if (await db.Payments.AnyAsync(x => x.CollectionOperationId == request.OperationId, ct)) throw new PaymentException(409, "payment_operation_exists");
            var payments = await db.Payments.Where(x => ids.Contains(x.OrderId)).ToArrayAsync(ct);
            if (payments.Length != ids.Length || payments.Any(x => x.DriverId != request.DriverId || x.Status != "Collected" || x.SettlementId != null) ||
                payments.Sum(x => x.Amount) != request.AmountReceived) throw new PaymentException(409, "settlement_amount_or_state_conflict");
            if (await db.Settlements.AnyAsync(x => x.Reference == reference, ct)) throw new PaymentException(409, "settlement_reference_exists");
            var settlement = new CashSettlement { Id = request.OperationId, DriverId = request.DriverId, ActorUserId = actor,
                Amount = request.AmountReceived, Reference = reference, Fingerprint = hash, CreatedAt = time.GetUtcNow() };
            db.Settlements.Add(settlement); foreach (var payment in payments) payment.SettlementId = settlement.Id;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new SettlementResponse(settlement.Id, settlement.DriverId, settlement.Amount, reference, ids, settlement.CreatedAt);
        });
    }
    private void RequireTransaction()
    { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Payment lifecycle changes require the order transaction coordinator."); }
    private async Task LocksAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
        {
            var resource = "velo:payments:" + key;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Payment lock unavailable.', 1;
                """, ct);
        }
    }
    private static PaymentReceipt Map(CodPayment p) => new(p.OrderId, p.Amount, p.Currency, p.Status, p.DriverId, p.CollectionOperationId, p.CollectedAt, p.SettlementId);
}
