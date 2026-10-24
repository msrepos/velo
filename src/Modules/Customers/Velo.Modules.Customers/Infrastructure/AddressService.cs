using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Customers.Application;
using Velo.Modules.Customers.Domain;
using Velo.Modules.Customers.Infrastructure.Persistence;

namespace Velo.Modules.Customers.Infrastructure;

public sealed class AddressService(CustomersDbContext db, TimeProvider time)
{
    public async Task<AddressResponse[]> ListAsync(Guid customer, CancellationToken ct) =>
        (await db.Addresses.AsNoTracking().Where(x => x.CustomerId == customer)
            .OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToArrayAsync(ct)).Select(Map).ToArray();

    public async Task<AddressResponse> GetAsync(Guid customer, Guid id, CancellationToken ct) =>
        Map(await OwnedAsync(customer, id, ct));

    public Task<AddressResponse> SaveAsync(Guid customer, Guid? id, AddressRequest request, CancellationToken ct)
    {
        var label = Text(request.Label, 80);
        var name = Text(request.RecipientName, 160);
        var phone = request.PhoneNumber?.Trim();
        if (phone is null || !Regex.IsMatch(phone, "^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant))
            throw new CustomerException(400, "invalid_phone_number");
        var city = Text(request.City, 100);
        var area = Text(request.Area, 160);
        var street = Text(request.Street, 300);
        var building = Text(request.Building, 80);
        var floor = Optional(request.Floor, 40);
        var apartment = Optional(request.Apartment, 40);
        var instructions = Optional(request.Instructions, 500);
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 ||
            decimal.Round(request.Latitude, 6) != request.Latitude || decimal.Round(request.Longitude, 6) != request.Longitude)
            throw new CustomerException(400, "invalid_coordinates");
        if (id is not null && request.Version?.Length != 8) throw new CustomerException(400, "invalid_version");
        var addressId = id ?? Guid.NewGuid();
        return MutateAsync(customer, async () =>
        {
            CustomerAddress item;
            if (id is null)
            {
                if (await db.Addresses.CountAsync(x => x.CustomerId == customer, ct) >= 20)
                    throw new CustomerException(409, "address_limit_reached");
                item = new CustomerAddress { Id = addressId, CustomerId = customer, Label = label,
                    RecipientName = name, PhoneNumber = phone, City = city, Area = area, Street = street,
                    Building = building, CreatedAt = time.GetUtcNow() };
                db.Addresses.Add(item);
            }
            else
            {
                item = await OwnedAsync(customer, id.Value, ct);
                if (!item.RowVersion.SequenceEqual(request.Version!)) throw new CustomerException(409, "address_version_conflict");
            }
            var makeDefault = request.IsDefault || !await db.Addresses.AnyAsync(x => x.CustomerId == customer && x.IsDefault, ct);
            // Keep the current default when PUT uses isDefault=false. Switching is explicit.
            if (makeDefault && !item.IsDefault)
                await db.Addresses.Where(x => x.CustomerId == customer && x.IsDefault)
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsDefault, false), ct);
            item.Label = label; item.RecipientName = name; item.PhoneNumber = phone;
            item.City = city; item.Area = area; item.Street = street; item.Building = building;
            item.Floor = floor; item.Apartment = apartment; item.Instructions = instructions;
            item.Latitude = request.Latitude; item.Longitude = request.Longitude;
            item.IsDefault = makeDefault || item.IsDefault;
            item.UpdatedAt = time.GetUtcNow();
            if (id is not null) db.Entry(item).Property(x => x.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct);
            return Map(item);
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid customer, Guid id, CancellationToken ct) => MutateAsync(customer, async () =>
    {
        var item = await OwnedAsync(customer, id, ct);
        db.Addresses.Remove(item);
        await db.SaveChangesAsync(ct);
        if (item.IsDefault)
        {
            var replacement = await db.Addresses.Where(x => x.CustomerId == customer)
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (replacement is not null) { replacement.IsDefault = true; replacement.UpdatedAt = time.GetUtcNow(); }
        }
        return true;
    }, ct);

    private async Task<CustomerAddress> OwnedAsync(Guid customer, Guid id, CancellationToken ct) =>
        await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customer, ct)
            ?? throw new CustomerException(404, "address_not_found");

    private Task<T> MutateAsync<T>(Guid customer, Func<Task<T>> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var key = "velo:customers:addresses:" + customer.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Address lock unavailable.', 1;
                """, ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });

    private static string Text(string? value, int length) => Optional(value, length)
        ?? throw new CustomerException(400, "invalid_address");
    private static string? Optional(string? value, int length)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > length || value.Any(char.IsControl)) throw new CustomerException(400, "invalid_address");
        return value;
    }
    private static AddressResponse Map(CustomerAddress x) => new(x.Id, x.Label, x.RecipientName, x.PhoneNumber,
        x.City, x.Area, x.Street, x.Building, x.Latitude, x.Longitude, x.Floor, x.Apartment, x.Instructions, x.IsDefault, x.RowVersion);
}
