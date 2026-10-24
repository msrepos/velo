namespace Velo.Modules.Payments.Contracts;
public sealed record PaymentReceipt(Guid OrderId, decimal Amount, string Currency, string Status,
    Guid? DriverId, Guid? CollectionOperationId, DateTimeOffset? CollectedAt, Guid? SettlementId);
public interface ICodPayments
{
    Task CreateAsync(Guid orderId, Guid customerId, decimal amount, CancellationToken ct);
    Task CancelAsync(Guid orderId, CancellationToken ct);
    Task<PaymentReceipt> CollectAsync(Guid orderId, Guid driverId, Guid operationId, decimal amount, CancellationToken ct);
    Task<PaymentReceipt> GetAsync(Guid orderId, CancellationToken ct);
}
public sealed class PaymentException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
