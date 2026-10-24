namespace Velo.Modules.Customers.Contracts;

public sealed record DeliveryAddress(Guid Id, string RecipientName, string PhoneNumber, string City, string Area,
    string Street, string Building, string? Floor, string? Apartment, string? Instructions,
    decimal Latitude, decimal Longitude, byte[] Version);

public interface ICustomerAddresses
{
    Task<DeliveryAddress?> GetAsync(Guid customerId, Guid addressId, CancellationToken ct);
}
