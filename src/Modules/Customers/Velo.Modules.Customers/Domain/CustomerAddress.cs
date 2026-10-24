namespace Velo.Modules.Customers.Domain;

public sealed class CustomerAddress
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public required string Label { get; set; }
    public required string RecipientName { get; set; }
    public required string PhoneNumber { get; set; }
    public required string City { get; set; }
    public required string Area { get; set; }
    public required string Street { get; set; }
    public required string Building { get; set; }
    public string? Floor { get; set; }
    public string? Apartment { get; set; }
    public string? Instructions { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
