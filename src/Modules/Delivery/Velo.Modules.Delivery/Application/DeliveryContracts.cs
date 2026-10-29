using System.Text.Json.Serialization;
namespace Velo.Modules.Delivery.Application;
public sealed record CreateDriverRequest(Guid UserId, string Name);
public sealed record UpdateDriverRequest(string Name, bool IsActive, byte[] Version);
public sealed record DriverResponse(Guid Id, Guid UserId, string Name, bool IsActive, byte[] Version);
public sealed record DriverPage(DriverResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record CreateCrewRequest(Guid UserId, string Name);
public sealed record UpdateCrewRequest(string Name, bool IsActive, byte[] Version);
public sealed record CrewResponse(Guid Id, Guid UserId, string Name, bool IsActive, byte[] Version);
public sealed record CrewPage(CrewResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record CreateVanDispatchRequest(Guid OperationId, Guid WarehouseId, Guid DriverId, Guid CrewMemberId);
public sealed record EndVanDispatchRequest(byte[] Version);
public sealed record VanDispatchResponse(Guid Id, Guid WarehouseId, string VanCode, string VanName,
    Guid DriverId, string DriverName, Guid CrewMemberId, string CrewMemberName,
    DateTimeOffset StartedAt, Guid StartedBy, DateTimeOffset? EndedAt, Guid? EndedBy, byte[] Version);
public sealed record VanDispatchPage(VanDispatchResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record LocationRequest([property: JsonRequired] decimal Latitude, [property: JsonRequired] decimal Longitude);
public sealed record DeliveryCodeResponse(string Code, DateTimeOffset ExpiresAt);
public sealed record TrackingResponse(Guid OrderId, string Status, string DriverName, decimal? Latitude, decimal? Longitude, DateTimeOffset? LocationAt);
public sealed record DriverDeliveryResponse(Guid OrderId, string Status, decimal AmountDue, Contracts.Dropoff? Dropoff,
    DateTimeOffset AssignedAt, DateTimeOffset? DispatchedAt, string? FailureReason, int Attempt,
    DateTimeOffset? TravelStartedAt = null, DateTimeOffset? ArrivedAt = null);
public sealed record DriverDeliveryPage(DriverDeliveryResponse[] Items, int Page, int PageSize, int TotalCount);
public static class DeliveryPolicies
{
    public const string Manage = "Delivery.Manage";
    public const string Permission = "delivery.manage";
}
