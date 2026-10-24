using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Catalog.Contracts;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Domain;
using Velo.Modules.Orders.Infrastructure.Persistence;

namespace Velo.Modules.Orders.Infrastructure;

public sealed class CartService(OrdersDbContext db, ICatalogOffers catalog, TimeProvider time)
{
    public Task<CartResponse> GetAsync(Guid customer, CancellationToken ct) =>
        LockedAsync(customer, async () => await SnapshotAsync(customer, ct), ct);

    public Task<CartResponse> SetItemAsync(Guid customer, Guid variant, SetCartItemRequest request,
        string? expected, CancellationToken ct)
    {
        if (variant == Guid.Empty || request.Quantity is < 1 or > 1000) throw new CartException(400, "invalid_cart_item");
        return ChangeAsync(customer, expected, async cart =>
        {
            var offers = await catalog.GetAsync([variant], ct);
            if (!offers.TryGetValue(variant, out var offer) || !offer.IsPublished)
                throw new CartException(409, "variant_unavailable");
            var item = cart.Items.SingleOrDefault(x => x.VariantId == variant);
            if (item is null)
            {
                if (cart.Items.Count >= 100) throw new CartException(409, "cart_item_limit_reached");
                cart.Items.Add(new CartItem { CustomerId = customer, VariantId = variant, Quantity = request.Quantity });
            }
            else item.Quantity = request.Quantity;
        }, ct);
    }

    public Task<CartResponse> RemoveItemAsync(Guid customer, Guid variant, string? expected, CancellationToken ct) =>
        ChangeAsync(customer, expected, cart =>
        {
            var item = cart.Items.SingleOrDefault(x => x.VariantId == variant);
            if (item is not null) { cart.Items.Remove(item); db.CartItems.Remove(item); }
            return Task.CompletedTask;
        }, ct);

    public Task<CartResponse> ClearAsync(Guid customer, string? expected, CancellationToken ct) =>
        ChangeAsync(customer, expected, cart =>
        {
            db.CartItems.RemoveRange(cart.Items);
            cart.Items.Clear();
            return Task.CompletedTask;
        }, ct);

    private Task<CartResponse> ChangeAsync(Guid customer, string? expected, Func<Cart, Task> change, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expected)) throw new CartException(428, "cart_version_required");
        // Exactly one quoted ETag is accepted; no wildcard can bypass lost-update protection.
        return LockedAsync(customer, async () =>
        {
            var cart = await db.Carts.Include(x => x.Items).SingleOrDefaultAsync(x => x.CustomerId == customer, ct);
            var current = (await SnapshotAsync(customer, ct)).Version;
            if (expected != '"' + current + '"') throw new CartException(412, "cart_version_conflict");
            if (cart is null) { cart = new Cart { CustomerId = customer }; db.Carts.Add(cart); }
            await change(cart);
            cart.UpdatedAt = time.GetUtcNow();
            if (db.Entry(cart).State != EntityState.Added) db.Entry(cart).Property(x => x.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct);
            return await SnapshotAsync(customer, ct);
        }, ct);
    }

    private async Task<CartResponse> SnapshotAsync(Guid customer, CancellationToken ct)
    {
        var cart = await db.Carts.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.CustomerId == customer, ct);
        if (cart is null) return new("empty", "EGP", 0, false, []);
        var offers = await catalog.GetAsync(cart.Items.Select(x => x.VariantId).ToArray(), ct);
        var items = cart.Items.OrderBy(x => x.VariantId).Select(x =>
        {
            offers.TryGetValue(x.VariantId, out var offer);
            return new CartItemResponse(x.VariantId, x.Quantity, offer?.ProductName, offer?.VariantName,
                offer?.Sku, offer?.Price, offer?.Price * x.Quantity, offer?.IsPublished == true);
        }).ToArray();
        var unavailable = items.Any(x => !x.IsAvailable);
        // Include current offers so a price/publication change also invalidates the ETag.
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(items)));
        return new(Convert.ToBase64String(cart.RowVersion) + "-" + fingerprint, "EGP",
            unavailable ? null : items.Sum(x => x.LineTotal ?? 0), unavailable, items);
    }

    private Task<T> LockedAsync<T>(Guid customer, Func<Task<T>> action, CancellationToken ct)
    {
        // The checkout coordinator already holds this customer's cart lock.
        if (db.Database.CurrentTransaction is not null) return action();
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var key = "velo:orders:cart:" + customer.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Cart lock unavailable.', 1;
                """, ct);
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
