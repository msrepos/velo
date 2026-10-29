using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Velo.Modules.Customers.Contracts;
using Velo.Modules.Inventory.Contracts;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Domain;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Payments.Contracts;
using Velo.Modules.Notifications.Contracts;
namespace Velo.Modules.Orders.Infrastructure;

public sealed partial class OrderService(OrdersDbContext db, CartService carts, ICustomerAddresses addresses,
    IOrderStock stock, IOrderTransaction transaction, IOptions<OrderOptions> options, TimeProvider time,
    IDeliveries deliveries, ICodPayments payments, IInAppNotifications notifications)
{
    public Task<CheckoutPreview> PreviewAsync(Guid customer, CheckoutPreviewRequest request, CancellationToken ct)
    {
        if (request.AddressId == Guid.Empty || request.AddressVersion?.Length != 8) throw new OrderException(400, "invalid_checkout");
        return transaction.RunAsync(customer, Guid.NewGuid(), async () =>
        {
            var settings = options.Value;
            if (!settings.EnableCheckout) throw new OrderException(503, "checkout_disabled");
            var address = await addresses.GetAsync(customer, request.AddressId, ct) ?? throw new OrderException(404, "address_not_found");
            if (!address.Version.SequenceEqual(request.AddressVersion!)) throw new OrderException(409, "address_version_conflict");
            var cart = await carts.GetAsync(customer, ct);
            if (cart.Items.Length == 0) throw new OrderException(409, "cart_empty");
            if (cart.HasUnavailableItems || cart.Subtotal is null) throw new OrderException(409, "variant_unavailable");
            var total = cart.Subtotal.Value + settings.DeliveryFee;
            if (total > 1_000_000m) throw new OrderException(400, "order_total_limit_exceeded");
            var warehouse = await stock.FindWarehouseAsync(address.Latitude, address.Longitude, settings.DeliveryRadiusKm,
                cart.Items.Select(x => new StockItem(x.VariantId, x.Quantity)).ToArray(), ct)
                ?? throw new OrderException(409, "no_fulfillment_warehouse");
            return new CheckoutPreview(cart, address, warehouse, settings.DeliveryFee, total, settings.ReservationMinutes);
        }, ct);
    }

    public Task<CheckoutResult> CheckoutAsync(Guid customer, CheckoutRequest request, string? cartEtag, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty || request.AddressId == Guid.Empty || request.AddressVersion?.Length != 8 ||
            request.ExpectedTotal <= 0 || decimal.Round(request.ExpectedTotal, 2) != request.ExpectedTotal)
            throw new OrderException(400, "invalid_checkout");
        if (request.PaymentMethod != "CashOnDelivery") throw new OrderException(400, "unsupported_payment_method");
        if (string.IsNullOrWhiteSpace(cartEtag)) throw new OrderException(428, "cart_version_required");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Request = request, CartEtag = cartEtag })));
        return transaction.RunAsync(customer, request.OperationId, async () =>
        {
            var previous = await Query().SingleOrDefaultAsync(x => x.Id == request.OperationId, ct);
            if (previous is not null)
            {
                if (previous.CustomerId != customer || previous.Fingerprint != fingerprint)
                    throw new OrderException(409, "checkout_idempotency_conflict");
                await ExpireAsync(previous, ct);
                return new CheckoutResult(Map(previous), true);
            }
            var settings = options.Value;
            if (!settings.EnableCheckout) throw new OrderException(503, "checkout_disabled");
            var address = await addresses.GetAsync(customer, request.AddressId, ct)
                ?? throw new OrderException(404, "address_not_found");
            if (!address.Version.SequenceEqual(request.AddressVersion!)) throw new OrderException(409, "address_version_conflict");
            var cart = await carts.GetAsync(customer, ct);
            if (cartEtag != '"' + cart.Version + '"') throw new OrderException(412, "cart_version_conflict");
            if (cart.Items.Length == 0) throw new OrderException(409, "cart_empty");
            if (cart.HasUnavailableItems || cart.Subtotal is null) throw new OrderException(409, "variant_unavailable");
            if (cart.Subtotal + settings.DeliveryFee > 1_000_000m) throw new OrderException(400, "order_total_limit_exceeded");
            if (request.ExpectedTotal != cart.Subtotal.Value + settings.DeliveryFee)
                throw new OrderException(409, "checkout_total_changed");
            var items = cart.Items.Select(x => new StockItem(x.VariantId, x.Quantity)).ToArray();
            var warehouse = await stock.FindWarehouseAsync(address.Latitude, address.Longitude, settings.DeliveryRadiusKm, items, ct)
                ?? throw new OrderException(409, "no_fulfillment_warehouse");
            var reservation = await stock.ReserveAsync(request.OperationId, customer, warehouse, items, settings.ReservationMinutes, ct);
            var now = time.GetUtcNow();
            var order = new CustomerOrder
            {
                Id = request.OperationId, CustomerId = customer, WarehouseId = warehouse, ReservationId = reservation.Id,
                Fingerprint = fingerprint, AddressJson = JsonSerializer.Serialize(address), Subtotal = cart.Subtotal.Value,
                DeliveryFee = settings.DeliveryFee, Total = cart.Subtotal.Value + settings.DeliveryFee,
                CreatedAt = now, UpdatedAt = now, ExpiresAt = reservation.ExpiresAt,
                Lines = cart.Items.Select(x => new OrderLine { OrderId = request.OperationId, VariantId = x.VariantId,
                    ProductName = x.ProductName!, VariantName = x.VariantName!, Sku = x.Sku!, Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice!.Value, LineTotal = x.LineTotal!.Value }).ToArray()
            };
            await payments.CreateAsync(order.Id, customer, order.Total, ct);
            await EventAsync(order, "Pending", customer, null, ct);
            db.Orders.Add(order);
            // This joins the same SQL transaction and holds the existing cart lock.
            await carts.ClearAsync(customer, cartEtag, ct);
            await db.SaveChangesAsync(ct);
            return new CheckoutResult(Map(order), false);
        }, ct);
    }

    public Task<OrderResponse> GetAsync(Guid customer, Guid id, CancellationToken ct) =>
        transaction.RunAsync(customer, id, async () =>
        {
            var order = await OwnedAsync(customer, id, ct);
            await ExpireAsync(order, ct);
            return Map(order);
        }, ct);

    public async Task<OrderPage> ListAsync(Guid customer, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new OrderException(400, "invalid_pagination");
        var query = db.Orders.AsNoTracking().Where(x => x.CustomerId == customer);
        var count = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(x => x.Id).ToArrayAsync(ct);
        var items = new List<OrderResponse>();
        foreach (var id in ids) items.Add(await GetAsync(customer, id, ct));
        return new(items.ToArray(), page, pageSize, count);
    }

    public async Task<OrderResponse> AdminGetAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Orders.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.CustomerId).SingleOrDefaultAsync(ct)
            ?? throw new OrderException(404, "order_not_found");
        return await GetAsync(customer, id, ct);
    }

    public async Task<OrderPage> AdminListAsync(string? status, int page, int pageSize, CancellationToken ct, Guid? warehouseId = null)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new OrderException(400, "invalid_pagination");
        if (status is not (null or "Pending" or "Confirmed" or "Preparing" or "ReadyForDispatch" or "OutForDelivery" or "Delivered" or "DeliveryFailed" or "Returned" or "Cancelled" or "Expired"))
            throw new OrderException(400, "invalid_order_status");
        var query = db.Orders.AsNoTracking();
        if (warehouseId is not null) query = query.Where(x => x.WarehouseId == warehouseId);
        var now = time.GetUtcNow();
        if (status == "Pending") query = query.Where(x => x.Status == "Pending" && x.ExpiresAt > now);
        else if (status == "Expired") query = query.Where(x => x.Status == "Expired" || (x.Status == "Pending" && x.ExpiresAt <= now));
        else if (status is not null) query = query.Where(x => x.Status == status);
        var count = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(x => new { x.CustomerId, x.Id }).ToArrayAsync(ct);
        var items = new List<OrderResponse>();
        foreach (var order in orders) items.Add(await GetAsync(order.CustomerId, order.Id, ct));
        return new(items.ToArray(), page, pageSize, count);
    }

    public Task<OrderResponse> CancelAsync(Guid customer, Guid id, CancelOrderRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500 || reason.Any(char.IsControl))
            throw new OrderException(400, "invalid_cancellation_reason");
        return transaction.RunAsync(customer, id, async () =>
        {
            var order = await OwnedAsync(customer, id, ct);
            await ExpireAsync(order, ct);
            if (order.Status is "Cancelled" or "Expired") return Map(order);
            if (order.Status != "Pending") throw new OrderException(409, "invalid_order_state");
            var reservation = await stock.FinishAsync(order.ReservationId, false, customer, ct);
            await payments.CancelAsync(order.Id, ct); order.PaymentStatus = "Cancelled";
            await EventAsync(order, reservation.Status == "Expired" ? "Expired" : "Cancelled", customer, reason, ct);
            await db.SaveChangesAsync(ct);
            return Map(order);
        }, ct);
    }

    public async Task<OrderResponse> ConfirmAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var customer = await db.Orders.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.CustomerId).SingleOrDefaultAsync(ct)
            ?? throw new OrderException(404, "order_not_found");
        var result = await transaction.RunAsync(customer, id, async () =>
        {
            var order = await OwnedAsync(customer, id, ct);
            await ExpireAsync(order, ct);
            if (order.Status != "Pending") return Map(order);
            var reservation = await stock.FinishAsync(order.ReservationId, true, actor, ct);
            if (reservation.Status != "Committed") throw new OrderException(409, "invalid_reservation_state");
            await EventAsync(order, "Confirmed", actor, null, ct);
            await AssignVanDriverAsync(order, ct);
            await db.SaveChangesAsync(ct);
            return Map(order);
        }, ct);
        // Expiry changes must commit even when the attempted confirmation is rejected.
        if (result.Status != "Confirmed") throw new OrderException(409, "invalid_order_state");
        return result;
    }

    public async Task ExpireDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var due = await db.Orders.AsNoTracking().Where(x => x.Status == "Pending" && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(100).Select(x => new { x.CustomerId, x.Id }).ToArrayAsync(ct);
        foreach (var order in due) await GetAsync(order.CustomerId, order.Id, ct);
    }

    private async Task ExpireAsync(CustomerOrder order, CancellationToken ct)
    {
        if (order.Status != "Pending" || order.ExpiresAt > time.GetUtcNow()) return;
        await payments.CreateAsync(order.Id, order.CustomerId, order.Total, ct);
        var reservation = await stock.FinishAsync(order.ReservationId, false, order.CustomerId, ct);
        if (reservation.Status is not ("Released" or "Expired")) throw new OrderException(409, "invalid_reservation_state");
        await payments.CancelAsync(order.Id, ct); order.PaymentStatus = "Cancelled";
        await EventAsync(order, "Expired", null, "Confirmation deadline elapsed", ct);
        await db.SaveChangesAsync(ct);
    }
    private IQueryable<CustomerOrder> Query() => db.Orders.Include(x => x.Lines).Include(x => x.Events).AsSplitQuery();
    private async Task<CustomerOrder> OwnedAsync(Guid customer, Guid id, CancellationToken ct)
    {
        var order = await Query().SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customer, ct)
            ?? throw new OrderException(404, "order_not_found");
        // Lazily establishes COD records for orders created before the Payments module existed.
        await payments.CreateAsync(order.Id, order.CustomerId, order.Total, ct);
        if (order.Status is "Cancelled" or "Expired" or "Returned")
        { await payments.CancelAsync(order.Id, ct); order.PaymentStatus = "Cancelled"; }
        return order;
    }
    private async Task EventAsync(CustomerOrder order, string status, Guid? actor, string? reason, CancellationToken ct)
    {
        order.Status = status; order.UpdatedAt = time.GetUtcNow();
        order.Events.Add(new OrderEvent { OrderId = order.Id, Sequence = order.Events.Count + 1,
            Status = status, ActorUserId = actor, Reason = reason, CreatedAt = time.GetUtcNow() });
        var (title, body) = status switch
        {
            "Pending" => ("Order received", "We have received your order."),
            "Confirmed" => ("Order confirmed", "Your order has been confirmed."),
            "Preparing" => ("Preparing your order", "We are preparing your order."),
            "ReadyForDispatch" => ("Order ready", "Your order is ready for delivery."),
            "OutForDelivery" => ("On the way", "Your order is on its way."),
            "Delivered" => ("Order delivered", "Your order has been delivered."),
            "DeliveryFailed" => ("Delivery update", "We could not complete delivery. Our team will follow up."),
            "Returned" => ("Order returned", "Your undelivered order has been returned."),
            "Expired" => ("Order expired", "Your order could not be confirmed in time."),
            _ => ("Order cancelled", "Your order has been cancelled.")
        };
        await notifications.QueueAsync(order.CustomerId, $"order:{order.Id:N}:{order.Events.Count}", "Order." + status,
            title, body, JsonSerializer.Serialize(new { orderId = order.Id, status }), ct);
    }
    private static OrderResponse Map(CustomerOrder x) => new(x.Id, x.WarehouseId, x.ReservationId, x.Status, x.Currency,
        x.PaymentMethod, x.PaymentStatus, x.Subtotal, x.DeliveryFee, x.Total, JsonSerializer.Deserialize<DeliveryAddress>(x.AddressJson)!,
        x.CreatedAt, x.ExpiresAt, x.Lines.OrderBy(l => l.VariantId).Select(l => new OrderLineResponse(l.VariantId,
            l.ProductName, l.VariantName, l.Sku, l.Quantity, l.UnitPrice, l.LineTotal)).ToArray(),
        x.Events.OrderBy(e => e.Sequence).Select(e => new OrderEventResponse(e.Status, e.Reason, e.CreatedAt)).ToArray());
}
