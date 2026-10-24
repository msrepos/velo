namespace Velo.Modules.Catalog.Domain;

public sealed class Product
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProductVariant> Variants { get; set; } = [];
}
