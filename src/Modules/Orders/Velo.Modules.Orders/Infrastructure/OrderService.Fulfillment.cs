using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Customers.Contracts;
using Velo.Modules.Inventory.Contracts;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Domain;
namespace Velo.Modules.Orders.Infrastructure;
public sealed partial class OrderService
{
    public async Task<OrderResponse> PrepareAsync(Guid orderId, Guid actor, bool ready, CancellationToken ct)
    {
        var owner = await OwnerAsync(orderId, ct);
        return await transaction.RunAsync(owner, orderId, async () =>
        {
            var order = await OwnedAsync(owner, orderId, ct);
            var target = ready ? "ReadyForDispatch" : "Preparing";
            if (order.Status == target) return Map(order);
            if (order.Status != (ready ? "Preparing" : "Confirmed")) throw new OrderException(409, "invalid_order_state");
            await EventAsync(order, target, actor, null, ct);
            await AssignVanDriverAsync(order, ct);
            await db.SaveChangesAsync(ct); return Map(order);
        }, ct);
    }
    public async Task<OrderResponse> AssignAsync(Guid orderId, Guid actor, AssignDriverRequest request, CancellationToken ct)
    {
        if (request.DriverId == Guid.Empty) throw new OrderException(400, "invalid_driver_id");
        var owner = await OwnerAsync(orderId, ct);
        return await transaction.RunAsync(owner, orderId, async () =>
        {
            var order = await OwnedAsync(owner, orderId, ct);
            if (order.Status is not ("Confirmed" or "Preparing" or "ReadyForDispatch" or "DeliveryFailed")) throw new OrderException(409, "invalid_order_state");
            var address = JsonSerializer.Deserialize<DeliveryAddress>(order.AddressJson)!;
            var assignment = await deliveries.AssignAsync(orderId, owner, request.DriverId,
                new(address.RecipientName, address.PhoneNumber, address.City, address.Area, address.Street, address.Building,
                    address.Floor, address.Apartment, address.Instructions, address.Latitude, address.Longitude), order.Total, ct);
            if (!assignment.Replayed)
            {
                await EventAsync(order, order.Status == "DeliveryFailed" ? "ReadyForDispatch" : order.Status, actor, "Delivery assigned", ct);
                await notifications.QueueAsync(assignment.DriverUserId, $"assignment:{orderId:N}:{order.Events.Count}", "Delivery.Assigned",
                    "New delivery", "A delivery has been assigned to you.", JsonSerializer.Serialize(new { orderId }), ct);
            }
            await db.SaveChangesAsync(ct); return Map(order);
        }, ct);
    }
    public async Task<OrderResponse> DispatchAsync(Guid actor, Guid orderId, CancellationToken ct)
    {
        var assignment = await deliveries.OwnAssignmentAsync(actor, orderId, ct);
        return await transaction.RunAsync(assignment.CustomerId, orderId, async () =>
        {
            await deliveries.OwnAssignmentAsync(actor, orderId, ct);
            var order = await OwnedAsync(assignment.CustomerId, orderId, ct);
            if (order.Status == "OutForDelivery") return Map(order);
            if (order.Status != "ReadyForDispatch") throw new OrderException(409, "invalid_order_state");
            await deliveries.DispatchAsync(orderId, assignment.DriverId, ct);
            await EventAsync(order, "OutForDelivery", actor, null, ct); await db.SaveChangesAsync(ct); return Map(order);
        }, ct);
    }
    public async Task<OrderResponse> CompleteAsync(Guid actor, Guid orderId, CompleteDeliveryRequest request, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty || request.DeliveryCode is null || request.DeliveryCode.Length != 6 ||
            request.DeliveryCode.Any(x => x is < '0' or > '9') || request.CollectedAmount <= 0 || decimal.Round(request.CollectedAmount, 2) != request.CollectedAmount)
            throw new OrderException(400, "invalid_delivery_completion");
        var assignment = await deliveries.OwnAssignmentAsync(actor, orderId, ct);
        var result = await transaction.RunAsync<(OrderResponse? Order, string? Error, int Status)>(assignment.CustomerId, orderId, async () =>
        {
            await deliveries.OwnAssignmentAsync(actor, orderId, ct);
            var order = await OwnedAsync(assignment.CustomerId, orderId, ct);
            if (order.Status == "Delivered")
            {
                await payments.CollectAsync(orderId, assignment.DriverId, request.OperationId, request.CollectedAmount, ct);
                return (Order: Map(order), Error: (string?)null, Status: 200);
            }
            if (order.Status != "OutForDelivery") throw new OrderException(409, "invalid_order_state");
            if (request.CollectedAmount != order.Total) throw new OrderException(409, "cash_amount_mismatch");
            var proof = await deliveries.CompleteAsync(orderId, assignment.DriverId, request.DeliveryCode, ct);
            if (!proof.Success) return (Order: (OrderResponse?)null, Error: proof.Code, Status: proof.Status);
            await payments.CollectAsync(orderId, assignment.DriverId, request.OperationId, request.CollectedAmount, ct);
            order.PaymentStatus = "Paid";
            await EventAsync(order, "Delivered", actor, null, ct); await db.SaveChangesAsync(ct);
            return (Order: Map(order), Error: (string?)null, Status: 200);
        }, ct);
        // Commit failed proof attempts without committing a delivery/payment transition.
        if (result.Error is not null) throw new OrderException(result.Status, result.Error);
        return result.Order!;
    }
    public async Task<OrderResponse> FailDeliveryAsync(Guid actor, Guid orderId, DeliveryFailureRequest request, CancellationToken ct)
    {
        var reason = Reason(request.Reason);
        var assignment = await deliveries.OwnAssignmentAsync(actor, orderId, ct);
        return await transaction.RunAsync(assignment.CustomerId, orderId, async () =>
        {
            await deliveries.OwnAssignmentAsync(actor, orderId, ct);
            var order = await OwnedAsync(assignment.CustomerId, orderId, ct);
            if (order.Status == "DeliveryFailed") return Map(order);
            if (order.Status != "OutForDelivery") throw new OrderException(409, "invalid_order_state");
            await deliveries.FailAsync(orderId, assignment.DriverId, reason, ct);
            await EventAsync(order, "DeliveryFailed", actor, reason, ct); await db.SaveChangesAsync(ct); return Map(order);
        }, ct);
    }
    public async Task<OrderResponse> AdminCancelAsync(Guid actor, Guid orderId, AdminCancelRequest request, CancellationToken ct)
    {
        var reason = Reason(request.Reason); var owner = await OwnerAsync(orderId, ct);
        return await transaction.RunAsync(owner, orderId, async () =>
        {
            var order = await OwnedAsync(owner, orderId, ct); await ExpireAsync(order, ct);
            if (order.Status is "Cancelled" or "Expired") return Map(order);
            if (order.Status is not ("Pending" or "Confirmed" or "Preparing" or "ReadyForDispatch")) throw new OrderException(409, "invalid_order_state");
            var target = "Cancelled";
            if (order.Status == "Pending")
            { var reservation = await stock.FinishAsync(order.ReservationId, false, actor, ct); if (reservation.Status == "Expired") target = "Expired"; }
            else await RestockAsync(order, request.RestockWarehouseId ?? order.WarehouseId, actor, reason, ct);
            await deliveries.CloseAsync(orderId, "Cancelled", ct); await payments.CancelAsync(orderId, ct);
            order.PaymentStatus = "Cancelled";
            await EventAsync(order, target, actor, reason, ct); await db.SaveChangesAsync(ct); return Map(order);
        }, ct);
    }
    public async Task<OrderResponse> ReturnAsync(Guid actor, Guid orderId, ReturnOrderRequest request, CancellationToken ct)
    {
        var reason = Reason(request.Reason); var owner = await OwnerAsync(orderId, ct);
        return await transaction.RunAsync(owner, orderId, async () =>
        {
            var order = await OwnedAsync(owner, orderId, ct);
            var decision = JsonSerializer.Serialize(new { request.Restock, WarehouseId = request.WarehouseId ?? order.WarehouseId, Reason = reason });
            if (order.Status == "Returned")
            {
                if (order.RestockDecisionJson != decision) throw new OrderException(409, "return_idempotency_conflict");
                return Map(order);
            }
            if (order.Status != "DeliveryFailed") throw new OrderException(409, "invalid_order_state");
            if (request.Restock) await RestockAsync(order, request.WarehouseId ?? order.WarehouseId, actor, reason, ct);
            await deliveries.CloseAsync(orderId, "Returned", ct); await payments.CancelAsync(orderId, ct); order.PaymentStatus = "Cancelled";
            order.RestockDecisionJson = decision;
            await EventAsync(order, "Returned", actor, reason + (request.Restock ? " (restocked)" : " (not restocked)"), ct);
            await db.SaveChangesAsync(ct); return Map(order);
        }, ct);
    }
    private Task RestockAsync(CustomerOrder order, Guid warehouse, Guid actor, string reason, CancellationToken ct) =>
        stock.RestoreAsync(order.Id, warehouse, order.Lines.Select(x => new StockItem(x.VariantId, x.Quantity)).ToArray(), actor, reason, ct);
    private async Task<Guid> OwnerAsync(Guid orderId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(x => x.Id == orderId).Select(x => (Guid?)x.CustomerId).SingleOrDefaultAsync(ct)
        ?? throw new OrderException(404, "order_not_found");
    private static string Reason(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 400 || value.Any(char.IsControl)) throw new OrderException(400, "invalid_reason");
        return value;
    }
}
