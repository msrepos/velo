namespace Velo.Modules.Catalog.Domain;

public sealed class ProductVariant
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
