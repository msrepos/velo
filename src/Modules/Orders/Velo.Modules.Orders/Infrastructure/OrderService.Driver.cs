using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Customers.Contracts;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Domain;

namespace Velo.Modules.Orders.Infrastructure;

public sealed partial class OrderService
{
    public async Task<DriverWorkResponse?> DriverWorkAsync(Guid userId, CancellationToken ct)
    {
        var assignment = await deliveries.OwnActiveAssignmentAsync(userId, ct);
        if (assignment is null) return null;
        return await transaction.RunAsync<DriverWorkResponse?>(assignment.CustomerId, assignment.OrderId, async () =>
        {
            var current = await deliveries.OwnAssignmentAsync(userId, assignment.OrderId, ct);
            if (current.Status is not ("Assigned" or "OutForDelivery" or "Failed")) return null;
            var order = await OwnedAsync(current.CustomerId, current.OrderId, ct);
            return new(Map(order), await deliveries.OwnJourneyAsync(userId, order.Id, ct));
        }, ct);
    }

    public async Task<DriverWorkResponse?> NextDriverWorkAsync(Guid userId, CancellationToken ct)
    {
        if (await deliveries.OwnActiveAssignmentAsync(userId, ct) is not null) return await DriverWorkAsync(userId, ct);
        var van = await deliveries.OwnVanAsync(userId, ct) ?? throw new OrderException(409, "active_van_dispatch_required");
        var candidates = await db.Orders.AsNoTracking().Where(x => x.WarehouseId == van.WarehouseId &&
            (x.Status == "Confirmed" || x.Status == "Preparing" || x.Status == "ReadyForDispatch"))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(100).Select(x => new { x.Id, x.CustomerId }).ToArrayAsync(ct);
        foreach (var candidate in candidates)
        {
            var assigned = await transaction.RunAsync(candidate.CustomerId, candidate.Id, async () =>
            {
                // The shift can change while a candidate is being selected.
                var currentVan = await deliveries.OwnVanAsync(userId, ct);
                if (currentVan?.WarehouseId != van.WarehouseId) return false;
                var order = await OwnedAsync(candidate.CustomerId, candidate.Id, ct);
                if (order.Status is not ("Confirmed" or "Preparing" or "ReadyForDispatch")) return false;
                var result = await AssignVanDriverAsync(order, ct);
                await db.SaveChangesAsync(ct);
                return result?.DriverId == van.DriverId;
            }, ct);
            if (assigned) return await DriverWorkAsync(userId, ct);
            if (await deliveries.OwnActiveAssignmentAsync(userId, ct) is not null) return await DriverWorkAsync(userId, ct);
        }
        return null;
    }

    private async Task<Assignment?> AssignVanDriverAsync(CustomerOrder order, CancellationToken ct)
    {
        var address = JsonSerializer.Deserialize<DeliveryAddress>(order.AddressJson)!;
        var result = await deliveries.TryAssignVanAsync(order.Id, order.CustomerId, order.WarehouseId,
            new(address.RecipientName, address.PhoneNumber, address.City, address.Area, address.Street, address.Building,
                address.Floor, address.Apartment, address.Instructions, address.Latitude, address.Longitude), order.Total, ct);
        if (result is { Replayed: false })
            await notifications.QueueAsync(result.DriverUserId, $"van-assignment:{order.Id:N}", "Delivery.Assigned",
                "New van order", "Your customer's location is ready. Travel while your crew prepares the order.",
                JsonSerializer.Serialize(new { orderId = order.Id }), ct);
        return result;
    }
}
