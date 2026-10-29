namespace Velo.Modules.Delivery.Contracts;
public sealed record Dropoff(string RecipientName, string PhoneNumber, string City, string Area, string Street,
    string Building, string? Floor, string? Apartment, string? Instructions, decimal Latitude, decimal Longitude);
public sealed record Assignment(Guid OrderId, Guid CustomerId, Guid DriverId, Guid DriverUserId, string Status, bool Replayed = false);
public sealed record ProofResult(bool Success, string? Code, int Status);
public sealed record DriverVan(Guid WarehouseId, Guid DriverId);
public sealed record DriverJourney(Guid OrderId, string Status, DateTimeOffset? TravelStartedAt, DateTimeOffset? ArrivedAt);
public interface IDeliveries
{
    Task<Assignment> AssignAsync(Guid orderId, Guid customerId, Guid driverId, Dropoff dropoff, decimal amount, CancellationToken ct);
    Task<Assignment> OwnAssignmentAsync(Guid userId, Guid orderId, CancellationToken ct);
    Task<Guid> OwnDriverIdAsync(Guid userId, CancellationToken ct);
    Task<Assignment?> OwnActiveAssignmentAsync(Guid userId, CancellationToken ct);
    Task<DriverVan?> OwnVanAsync(Guid userId, CancellationToken ct);
    Task<DriverJourney> OwnJourneyAsync(Guid userId, Guid orderId, CancellationToken ct);
    Task<Assignment?> TryAssignVanAsync(Guid orderId, Guid customerId, Guid warehouseId, Dropoff dropoff, decimal amount, CancellationToken ct);
    Task DispatchAsync(Guid orderId, Guid driverId, CancellationToken ct);
    Task<ProofResult> CompleteAsync(Guid orderId, Guid driverId, string proofCode, CancellationToken ct);
    Task FailAsync(Guid orderId, Guid driverId, string reason, CancellationToken ct);
    Task CloseAsync(Guid orderId, string status, CancellationToken ct);
}
public sealed class DeliveryException(int status, string code, int? retryAfter = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
}
