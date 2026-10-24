using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Catalog.Domain;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Customers.Application;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Orders.Application;
using Xunit;

namespace Velo.Commerce.IntegrationTests;

public sealed class CommerceTests(CommerceFixture fixture) : IClassFixture<CommerceFixture>
{
    private const string Addresses = "/api/v1/customers/me/addresses";
    private const string Cart = "/api/v1/cart";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_cannot_access_addresses_or_cart()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Addresses, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Cart, Ct)).StatusCode);
    }

    [Fact]
    public async Task Address_ownership_is_enforced_for_read_update_and_delete()
    {
        using var first = fixture.Client("first");
        using var second = fixture.Client("second");
        var address = await CreateAddress(first);
        var path = Addresses + "/" + address.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PutAsJsonAsync(path, Request() with { Version = address.Version }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.DeleteAsync(path, Ct)).StatusCode);
        Assert.DoesNotContain((await second.GetFromJsonAsync<AddressResponse[]>(Addresses, Ct))!, x => x.Id == address.Id);
        Assert.Equal(address.Id, (await first.GetFromJsonAsync<AddressResponse>(path, Ct))!.Id);
    }

    [Fact]
    public async Task Concurrent_first_addresses_have_exactly_one_default()
    {
        using var first = fixture.Client("first");
        using var anotherDevice = fixture.Client("first");
        await ClearAddresses(first);
        var responses = await Task.WhenAll(first.PostAsJsonAsync(Addresses, Request(), Ct),
            anotherDevice.PostAsJsonAsync(Addresses, Request(), Ct));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var items = (await first.GetFromJsonAsync<AddressResponse[]>(Addresses, Ct))!;
        Assert.Equal(2, items.Length);
        Assert.Single(items, x => x.IsDefault);
    }

    [Fact]
    public async Task Switching_default_invalidates_old_version_and_delete_promotes_remaining()
    {
        using var client = fixture.Client("first");
        await ClearAddresses(client);
        var original = await CreateAddress(client);
        var second = await CreateAddress(client, Request() with { Label = "Office", IsDefault = true });
        var items = (await client.GetFromJsonAsync<AddressResponse[]>(Addresses, Ct))!;
        Assert.Equal(second.Id, Assert.Single(items, x => x.IsDefault).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Addresses + "/" + original.Id,
            Request() with { Version = original.Version }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Addresses + "/" + second.Id, Ct)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<AddressResponse>(Addresses + "/" + original.Id, Ct))!.IsDefault);
    }

    [Fact]
    public async Task Address_edit_requires_latest_version_and_preserves_default()
    {
        using var client = fixture.Client("first");
        await ClearAddresses(client);
        var address = await CreateAddress(client);
        var path = Addresses + "/" + address.Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, Request(), Ct)).StatusCode);
        var edit = Request() with { Label = "Updated", Version = address.Version };
        var updated = await (await client.PutAsJsonAsync(path, edit, Ct)).Content.ReadFromJsonAsync<AddressResponse>(Ct);
        Assert.NotNull(updated);
        Assert.True(updated.IsDefault);
        Assert.Equal("Updated", updated.Label);
        Assert.NotEqual(Convert.ToBase64String(address.Version), Convert.ToBase64String(updated.Version));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, edit, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("01012345678", 30, 31)]
    [InlineData("+201012345678", 91, 31)]
    [InlineData("+201012345678", 30, -181)]
    public async Task Addresses_validate_phone_and_coordinates(string phone, decimal latitude, decimal longitude)
    {
        using var client = fixture.Client("first");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Addresses,
            Request() with { PhoneNumber = phone, Latitude = latitude, Longitude = longitude }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Addresses_reject_missing_coordinates_excess_precision_and_blank_fields()
    {
        using var client = fixture.Client("first");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Addresses,
            new { label = "Home", recipientName = "Test", phoneNumber = "+201012345678", city = "Cairo",
                area = "Nasr City", street = "Street", building = "1" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Addresses,
            Request() with { Latitude = 30.1234567m }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Addresses,
            Request() with { Street = " " }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Malformed_json_returns_client_error_instead_of_server_error()
    {
        using var client = fixture.Client("first");
        using var content = new StringContent("{broken", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Addresses, content, Ct)).StatusCode);
    }

    [Fact]
    public async Task Address_limit_is_enforced_and_deleting_all_leaves_empty_list()
    {
        using var client = fixture.Client("second");
        await ClearAddresses(client);
        for (var i = 0; i < 20; i++) await CreateAddress(client);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Addresses, Request(), Ct)).StatusCode);
        await ClearAddresses(client);
        Assert.Empty((await client.GetFromJsonAsync<AddressResponse[]>(Addresses, Ct))!);
    }

    [Fact]
    public async Task Cart_prices_are_server_calculated_and_setting_quantity_does_not_increment()
    {
        using var client = fixture.Client("first");
        await ClearCart(client);
        var variant = await Variant(12.25m);
        var cart = await GetCart(client);
        var reply = await Set(client, variant, 3, cart.Version);
        var result = await ReadCart(reply);
        Assert.Equal(36.75m, result.Subtotal);
        Assert.Equal("EGP", result.Currency);
        Assert.Equal(12.25m, Assert.Single(result.Items).UnitPrice);
        Assert.Equal('"' + result.Version + '"', reply.Headers.ETag!.Tag);
        result = await ReadCart(await Set(client, variant, 3, result.Version));
        Assert.Equal(3, Assert.Single(result.Items).Quantity);
    }

    [Fact]
    public async Task Cart_is_isolated_by_authenticated_customer()
    {
        using var first = fixture.Client("first");
        using var second = fixture.Client("second");
        await ClearCart(first); await ClearCart(second);
        var variant = await Variant();
        await ReadCart(await Set(first, variant, 2, (await GetCart(first)).Version));
        Assert.Empty((await GetCart(second)).Items);
        Assert.Single((await GetCart(first)).Items);
    }

    [Fact]
    public async Task Cart_requires_matching_version_for_all_mutations()
    {
        using var client = fixture.Client("first");
        await ClearCart(client);
        var variant = await Variant();
        Assert.Equal((HttpStatusCode)428, (await client.PutAsJsonAsync(Cart + "/items/" + variant,
            new SetCartItemRequest(1), Ct)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await client.DeleteAsync(Cart + "/items/" + variant, Ct)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await client.DeleteAsync(Cart, Ct)).StatusCode);
        var before = await GetCart(client);
        await ReadCart(await Set(client, variant, 2, before.Version));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Set(client, variant, 3, before.Version)).StatusCode);
        var wildcard = new HttpRequestMessage(HttpMethod.Delete, Cart);
        wildcard.Headers.TryAddWithoutValidation("If-Match", "*");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(wildcard, Ct)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_cart_edits_accept_one_and_reject_stale_device()
    {
        using var first = fixture.Client("first");
        using var secondDevice = fixture.Client("first");
        await ClearCart(first);
        var a = await Variant(); var b = await Variant();
        var version = (await GetCart(first)).Version;
        var replies = await Task.WhenAll(Set(first, a, 1, version), Set(secondDevice, b, 1, version));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Single((await GetCart(first)).Items);
    }

    [Fact]
    public async Task Catalog_price_changes_refresh_totals_and_invalidate_cart_version()
    {
        using var client = fixture.Client("first");
        await ClearCart(client);
        var variant = await Variant(10m);
        var before = await ReadCart(await Set(client, variant, 2, (await GetCart(client)).Version));
        using (var scope = fixture.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ProductVariants.Where(x => x.Id == variant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, 12.5m), Ct);
        var after = await GetCart(client);
        Assert.Equal(25m, after.Subtotal);
        Assert.NotEqual(before.Version, after.Version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Set(client, variant, 3, before.Version)).StatusCode);
    }

    [Fact]
    public async Task Unpublished_cart_items_remain_removable_and_block_subtotal()
    {
        using var client = fixture.Client("first");
        await ClearCart(client);
        var variant = await Variant();
        await ReadCart(await Set(client, variant, 2, (await GetCart(client)).Version));
        using (var scope = fixture.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ProductVariants.Where(x => x.Id == variant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false), Ct);
        var cart = await GetCart(client);
        Assert.True(cart.HasUnavailableItems); Assert.Null(cart.Subtotal);
        Assert.False(Assert.Single(cart.Items).IsAvailable);
        Assert.Equal(HttpStatusCode.Conflict, (await Set(client, variant, 3, cart.Version)).StatusCode);
        Assert.Empty((await ReadCart(await Delete(client, Cart + "/items/" + variant, cart.Version))).Items);
    }

    [Fact]
    public async Task Cart_rejects_unknown_variants_and_never_accepts_client_prices_or_reserves_stock()
    {
        using var client = fixture.Client("first");
        await ClearCart(client);
        var before = await GetCart(client);
        Assert.Equal(HttpStatusCode.Conflict, (await Set(client, Guid.NewGuid(), 1, before.Version)).StatusCode);
        var variant = await Variant(9m);
        var request = new HttpRequestMessage(HttpMethod.Put, Cart + "/items/" + variant)
        { Content = JsonContent.Create(new { quantity = 2, unitPrice = 0.01, subtotal = 0.02 }) };
        request.Headers.TryAddWithoutValidation("If-Match", '"' + before.Version + '"');
        Assert.Equal(18m, (await ReadCart(await client.SendAsync(request, Ct))).Subtotal);
        using var scope = fixture.Factory.Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(0, await inventory.Reservations.CountAsync(Ct));
        Assert.Equal(0, await inventory.Balances.CountAsync(Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task Cart_validates_quantity(int quantity)
    {
        using var client = fixture.Client("first");
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(client, Guid.NewGuid(), quantity, (await GetCart(client)).Version)).StatusCode);
    }

    [Fact]
    public async Task Clearing_cart_changes_version_and_old_version_cannot_restore_deleted_items()
    {
        using var client = fixture.Client("first");
        await ClearCart(client);
        var variant = await Variant();
        var before = await ReadCart(await Set(client, variant, 2, (await GetCart(client)).Version));
        var after = await ReadCart(await Delete(client, Cart, before.Version));
        Assert.Empty(after.Items); Assert.Equal(0m, after.Subtotal);
        Assert.NotEqual(before.Version, after.Version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Set(client, variant, 2, before.Version)).StatusCode);
    }

    private static AddressRequest Request() => new("Home", "Test Customer", "+201012345678", "Cairo", "Nasr City",
        "Test street", "10", 30.05m, 31.35m, Floor: "2", Apartment: "4");
    private static async Task<AddressResponse> CreateAddress(HttpClient client, AddressRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(Addresses, request ?? Request(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AddressResponse>(Ct))!;
    }
    private static async Task ClearAddresses(HttpClient client)
    {
        foreach (var item in (await client.GetFromJsonAsync<AddressResponse[]>(Addresses, Ct))!)
            (await client.DeleteAsync(Addresses + "/" + item.Id, Ct)).EnsureSuccessStatusCode();
    }
    private static async Task<CartResponse> GetCart(HttpClient client) =>
        (await client.GetFromJsonAsync<CartResponse>(Cart, Ct))!;
    private static async Task<CartResponse> ReadCart(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CartResponse>(Ct))!;
    }
    private static Task<HttpResponseMessage> Set(HttpClient client, Guid variant, int quantity, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Cart + "/items/" + variant)
            { Content = JsonContent.Create(new SetCartItemRequest(quantity)) };
        request.Headers.TryAddWithoutValidation("If-Match", '"' + version + '"');
        return client.SendAsync(request, Ct);
    }
    private static Task<HttpResponseMessage> Delete(HttpClient client, string path, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.TryAddWithoutValidation("If-Match", '"' + version + '"');
        return client.SendAsync(request, Ct);
    }
    private static async Task ClearCart(HttpClient client) =>
        await ReadCart(await Delete(client, Cart, (await GetCart(client)).Version));
    private async Task<Guid> Variant(decimal price = 10m)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = "Commerce " + suffix,
            NormalizedName = suffix.ToUpperInvariant(), IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), Name = "Bread", Category = category, IsActive = true };
        var variant = new ProductVariant { Id = Guid.NewGuid(), Name = "Pack", Sku = suffix,
            Product = product, Price = price, IsActive = true };
        db.ProductVariants.Add(variant); await db.SaveChangesAsync(Ct);
        return variant.Id;
    }
}
