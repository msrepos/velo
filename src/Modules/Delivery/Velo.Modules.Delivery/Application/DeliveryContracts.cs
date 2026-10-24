using System.Text.Json.Serialization;
namespace Velo.Modules.Delivery.Application;
public sealed record CreateDriverRequest(Guid UserId, string Name);
public sealed record UpdateDriverRequest(string Name, bool IsActive, byte[] Version);
public sealed record DriverResponse(Guid Id, Guid UserId, string Name, bool IsActive, byte[] Version);
public sealed record DriverPage(DriverResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record LocationRequest([property: JsonRequired] decimal Latitude, [property: JsonRequired] decimal Longitude);
public sealed record DeliveryCodeResponse(string Code, DateTimeOffset ExpiresAt);
public sealed record TrackingResponse(Guid OrderId, string Status, string DriverName, decimal? Latitude, decimal? Longitude, DateTimeOffset? LocationAt);
public sealed record DriverDeliveryResponse(Guid OrderId, string Status, decimal AmountDue, Contracts.Dropoff? Dropoff,
    DateTimeOffset AssignedAt, DateTimeOffset? DispatchedAt, string? FailureReason, int Attempt);
public sealed record DriverDeliveryPage(DriverDeliveryResponse[] Items, int Page, int PageSize, int TotalCount);
public static class DeliveryPolicies
{
    public const string Manage = "Delivery.Manage";
    public const string Permission = "delivery.manage";
}
