namespace Velo.Modules.Catalog.Domain;

public sealed class Category
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
