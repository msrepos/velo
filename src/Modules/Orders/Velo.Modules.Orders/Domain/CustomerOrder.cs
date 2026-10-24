namespace Velo.Modules.Orders.Domain;

public sealed class CustomerOrder
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ReservationId { get; set; }
    public required string Fingerprint { get; set; }
    public required string AddressJson { get; set; }
    public string Status { get; set; } = "Pending";
    public string Currency { get; set; } = "EGP";
    public string PaymentMethod { get; set; } = "CashOnDelivery";
    public string PaymentStatus { get; set; } = "Unpaid";
    public string? RestockDecisionJson { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<OrderLine> Lines { get; set; } = [];
    public ICollection<OrderEvent> Events { get; set; } = [];
}
public sealed class OrderLine
{
    public Guid OrderId { get; set; }
    public Guid VariantId { get; set; }
    public required string ProductName { get; set; }
    public required string VariantName { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
public sealed class OrderEvent
{
    public Guid OrderId { get; set; }
    public int Sequence { get; set; }
    public required string Status { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
