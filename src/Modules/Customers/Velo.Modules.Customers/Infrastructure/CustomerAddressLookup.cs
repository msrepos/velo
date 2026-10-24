using Microsoft.EntityFrameworkCore;
using Velo.Modules.Customers.Contracts;
using Velo.Modules.Customers.Infrastructure.Persistence;
namespace Velo.Modules.Customers.Infrastructure;

public sealed class CustomerAddressLookup(CustomersDbContext db) : ICustomerAddresses
{
    public Task<DeliveryAddress?> GetAsync(Guid customerId, Guid addressId, CancellationToken ct) =>
        db.Addresses.AsNoTracking().Where(x => x.Id == addressId && x.CustomerId == customerId)
            .Select(x => new DeliveryAddress(x.Id, x.RecipientName, x.PhoneNumber, x.City, x.Area, x.Street,
                x.Building, x.Floor, x.Apartment, x.Instructions, x.Latitude, x.Longitude, x.RowVersion))
            .SingleOrDefaultAsync(ct);
}
