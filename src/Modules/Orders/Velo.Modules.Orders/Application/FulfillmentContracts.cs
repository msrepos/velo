using System.Text.Json.Serialization;
namespace Velo.Modules.Orders.Application;
public sealed record AssignDriverRequest(Guid DriverId);
public sealed record DeliveryFailureRequest(string Reason);
public sealed record CompleteDeliveryRequest(Guid OperationId, string DeliveryCode, decimal CollectedAmount);
public sealed record AdminCancelRequest(string Reason, Guid? RestockWarehouseId = null);
public sealed record ReturnOrderRequest(string Reason, [property: JsonRequired] bool Restock, Guid? WarehouseId = null);
