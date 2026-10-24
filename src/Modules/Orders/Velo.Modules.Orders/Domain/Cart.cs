namespace Velo.Modules.Orders.Domain;

public sealed class Cart
{
    public Guid CustomerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<CartItem> Items { get; set; } = [];
}

public sealed class CartItem
{
    public Guid CustomerId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
}
