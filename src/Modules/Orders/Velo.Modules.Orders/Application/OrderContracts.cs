using Velo.Modules.Customers.Contracts;
using System.Text.Json.Serialization;
namespace Velo.Modules.Orders.Application;

public sealed record CheckoutRequest(Guid OperationId, Guid AddressId, byte[] AddressVersion,
    [property: JsonRequired] decimal ExpectedTotal, string PaymentMethod = "CashOnDelivery");
public sealed record CheckoutPreviewRequest(Guid AddressId, byte[] AddressVersion);
public sealed record CheckoutPreview(CartResponse Cart, DeliveryAddress Address, Guid WarehouseId,
    decimal DeliveryFee, decimal Total, int ReservationMinutes);
public sealed record CancelOrderRequest(string Reason);
public sealed record OrderLineResponse(Guid VariantId, string ProductName, string VariantName, string Sku,
    int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record OrderEventResponse(string Status, string? Reason, DateTimeOffset CreatedAt);
public sealed record OrderResponse(Guid Id, Guid WarehouseId, Guid ReservationId, string Status, string Currency,
    string PaymentMethod, string PaymentStatus, decimal Subtotal, decimal DeliveryFee, decimal Total,
    DeliveryAddress Address, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, OrderLineResponse[] Items, OrderEventResponse[] History);
public sealed record OrderPage(OrderResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record CheckoutResult(OrderResponse Order, bool Replayed);
public sealed class OrderException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public interface IOrderTransaction
{
    Task<T> RunAsync<T>(Guid customer, Guid operation, Func<Task<T>> action, CancellationToken ct);
}
public sealed class OrderOptions
{
    public bool EnableCheckout { get; set; }
    public bool EnableExpiryWorker { get; set; } = true;
    public decimal DeliveryFee { get; set; }
    public double DeliveryRadiusKm { get; set; }
    public int ReservationMinutes { get; set; } = 10;
}
public static class OrderPolicies
{
    public const string Manage = "Orders.Manage";
    public const string Permission = "orders.manage";
}
