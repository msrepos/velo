using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Velo.Modules.Catalog.Domain;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Customers.Application;
using Velo.Modules.Customers.Infrastructure.Persistence;
using Velo.Modules.Inventory.Contracts;
using Velo.Modules.Inventory.Domain;
using Velo.Modules.Inventory.Infrastructure;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Infrastructure;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Xunit;
namespace Velo.Orders.IntegrationTests;

public sealed partial class OrderTests(OrderFixture fixture) : IClassFixture<OrderFixture>
{
    private const string Api = "/api/v1/orders";
    private const string Addresses = "/api/v1/customers/me/addresses";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checkout_reserves_stock_saves_snapshots_and_clears_cart_atomically()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        var reply = await Checkout(customer, scenario);
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        var order = (await reply.Content.ReadFromJsonAsync<OrderResponse>(Ct))!;
        Assert.Equal("Pending", order.Status); Assert.Equal("Unpaid", order.PaymentStatus);
        Assert.Equal(20m, order.Subtotal); Assert.Equal(20m, order.DeliveryFee); Assert.Equal(40m, order.Total);
        Assert.Equal(scenario.Warehouse, order.WarehouseId);
        Assert.Equal("Test street", order.Address.Street);
        Assert.Equal("Pending", Assert.Single(order.History).Status);
        Assert.Empty((await Cart(customer)).Items);
        var balance = await Balance(scenario);
        Assert.Equal(10, balance.OnHand); Assert.Equal(2, balance.Reserved);
        using var scope = fixture.Factory.Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal("Orders", (await inventory.Reservations.SingleAsync(x => x.Id == order.ReservationId, Ct)).OwnerModule);
    }

    [Fact]
    public async Task Checkout_replay_returns_same_order_without_rechecking_deleted_address_or_changed_prices()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        var original = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        (await customer.DeleteAsync(Addresses + "/" + scenario.Address.Id, Ct)).EnsureSuccessStatusCode();
        using (var scope = fixture.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ProductVariants.Where(x => x.Id == scenario.Variant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, 25m).SetProperty(x => x.Name, "Changed"), Ct);
        var replay = await Read(await Checkout(customer, scenario), HttpStatusCode.OK);
        Assert.Equal(original.Id, replay.Id); Assert.Equal(original.Total, replay.Total);
        Assert.Equal("Pack", Assert.Single(replay.Items).VariantName);
        Assert.Equal(2, (await Balance(scenario)).Reserved);
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(customer, scenario with
            { Request = scenario.Request with { AddressId = Guid.NewGuid() } })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_checkout_retries_create_one_order_and_one_reservation()
    {
        using var customer = fixture.Client("first"); using var device = fixture.Client("first");
        var scenario = await Prepare(customer);
        var replies = await Task.WhenAll(Checkout(customer, scenario), Checkout(device, scenario));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(2, (await Balance(scenario)).Reserved);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Movements
            .CountAsync(x => x.OperationId == scenario.Request.OperationId && x.Kind == "Reserve", Ct));
    }

    [Fact]
    public async Task Confirm_requires_order_permission_and_retries_consume_stock_once()
    {
        using var customer = fixture.Client("first"); using var staff = fixture.Client("staff");
        using var inventory = fixture.Client("inventory"); using var catalog = fixture.Client("catalog");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        var path = $"/api/v1/admin/orders/{order.Id}/confirm";
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(path, null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inventory.PostAsync(path, null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalog.PostAsync(path, null, Ct)).StatusCode);
        var confirmed = await Read(await staff.PostAsync(path, null, Ct), HttpStatusCode.OK);
        Assert.Equal("Confirmed", confirmed.Status); Assert.Equal("Unpaid", confirmed.PaymentStatus);
        await Read(await staff.PostAsync(path, null, Ct), HttpStatusCode.OK);
        var balance = await Balance(scenario);
        Assert.Equal(8, balance.OnHand); Assert.Equal(0, balance.Reserved);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync(Api + "/" + order.Id + "/cancel",
            new CancelOrderRequest("Too late"), Ct)).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Movements
            .CountAsync(x => x.OperationId == order.Id && x.Kind == "Commit", Ct));
    }

    [Fact]
    public async Task Cancel_releases_reserved_stock_once_and_blocks_confirmation()
    {
        using var customer = fixture.Client("first"); using var staff = fixture.Client("staff");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        var path = Api + "/" + order.Id + "/cancel";
        var cancelled = await Read(await customer.PostAsJsonAsync(path, new CancelOrderRequest("Changed my mind"), Ct), HttpStatusCode.OK);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Changed my mind", cancelled.History.Last().Reason);
        await Read(await customer.PostAsJsonAsync(path, new CancelOrderRequest("Retry"), Ct), HttpStatusCode.OK);
        Assert.Equal(10, (await Balance(scenario)).OnHand); Assert.Equal(0, (await Balance(scenario)).Reserved);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsync($"/api/v1/admin/orders/{order.Id}/confirm", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Order_ownership_blocks_reads_cancellations_and_idempotency_key_reuse()
    {
        using var first = fixture.Client("first"); using var second = fixture.Client("second");
        var scenario = await Prepare(first);
        var order = await Read(await Checkout(first, scenario), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(Api + "/" + order.Id, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync(Api + "/" + order.Id + "/cancel",
            new CancelOrderRequest("Not mine"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(second, scenario)).StatusCode);
        var list = (await second.GetFromJsonAsync<OrderPage>(Api, Ct))!;
        Assert.DoesNotContain(list.Items, x => x.Id == order.Id);
    }

    [Fact]
    public async Task Checkout_rejects_another_customers_address_and_stale_address_version()
    {
        using var first = fixture.Client("first"); using var second = fixture.Client("second");
        var scenario = await Prepare(first);
        var otherAddress = await Address(second);
        Assert.Equal(HttpStatusCode.NotFound, (await Checkout(first, scenario with { Request = scenario.Request with
            { AddressId = otherAddress.Id, AddressVersion = otherAddress.Version } })).StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CustomersDbContext>().Addresses.Where(x => x.Id == scenario.Address.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Street, "Changed address"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(first, scenario)).StatusCode);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
        Assert.Single((await Cart(first)).Items);
    }

    [Fact]
    public async Task Changed_cart_prices_are_rejected_before_stock_or_order_changes()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        using (var scope = fixture.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ProductVariants.Where(x => x.Id == scenario.Variant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Price, 11m), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Checkout(customer, scenario)).StatusCode);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
        Assert.Single((await Cart(customer)).Items);
    }

    [Fact]
    public async Task No_eligible_stock_or_outside_radius_leaves_cart_unchanged()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer, stock: 1);
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(customer, scenario)).StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await inventory.Balances.Where(x => x.WarehouseId == scenario.Warehouse)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.OnHand, 10), Ct);
            await inventory.Warehouses.Where(x => x.Id == scenario.Warehouse)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Latitude, 35m), Ct);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(customer, scenario)).StatusCode);
        Assert.Single((await Cart(customer)).Items);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
    }

    [Fact]
    public async Task Concurrent_customers_cannot_checkout_the_same_last_unit()
    {
        using var first = fixture.Client("first"); using var second = fixture.Client("second");
        var a = await Prepare(first, quantity: 1, stock: 1);
        await ClearCart(second);
        var address = await Address(second);
        var bCart = await Set(second, a.Variant, 1, (await Cart(second)).Version);
        var b = a with { Address = address, Cart = bCart,
            Request = new CheckoutRequest(Guid.NewGuid(), address.Id, address.Version, 30m) };
        var replies = await Task.WhenAll(Checkout(first, a), Checkout(second, b));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, (await Balance(a)).Reserved);
        Assert.Equal(1, (await Balance(a)).OnHand);
    }

    [Fact]
    public async Task Expired_order_releases_stock_and_cannot_be_confirmed()
    {
        using var customer = fixture.Client("first"); using var staff = fixture.Client("staff");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsync($"/api/v1/admin/orders/{order.Id}/confirm", null, Ct)).StatusCode);
        var expired = (await customer.GetFromJsonAsync<OrderResponse>(Api + "/" + order.Id, Ct))!;
        Assert.Equal("Expired", expired.Status); Assert.Equal("Expired", expired.History.Last().Status);
        Assert.Equal(0, (await Balance(scenario)).Reserved); Assert.Equal(10, (await Balance(scenario)).OnHand);
        var replay = await Read(await Checkout(customer, scenario), HttpStatusCode.OK);
        Assert.Equal("Expired", replay.Status);
    }

    [Fact]
    public async Task Expiry_passes_can_repeat_without_duplicate_ledger_entries()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        using var first = fixture.Factory.Services.CreateScope(); using var second = fixture.Factory.Services.CreateScope();
        await Task.WhenAll(first.ServiceProvider.GetRequiredService<OrderService>().ExpireDueAsync(Ct),
            second.ServiceProvider.GetRequiredService<OrderService>().ExpireDueAsync(Ct));
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.Equal("Expired", (await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.SingleAsync(x => x.Id == order.Id, Ct)).Status);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Movements
            .CountAsync(x => x.OperationId == order.Id && x.Kind == "Expire", Ct));
    }

    [Fact]
    public async Task Inventory_admin_cannot_finish_an_orders_reservation_directly()
    {
        using var customer = fixture.Client("first"); using var inventory = fixture.Client("inventory");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        foreach (var verb in new[] { "commit", "release" })
            Assert.Equal(HttpStatusCode.Conflict, (await inventory.PostAsync($"/api/v1/admin/inventory/reservations/{order.ReservationId}/{verb}", null, Ct)).StatusCode);
        Assert.Equal(2, (await Balance(scenario)).Reserved);
    }

    [Fact]
    public async Task Failure_after_reserving_stock_rolls_back_order_stock_and_cart_together()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        await using var faulted = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IOrderStock>(sp => new FailingStock(new OrderStock(sp.GetRequiredService<InventoryService>(),
                sp.GetRequiredService<InventoryDbContext>()), failReserve: true))));
        using var client = fixture.Client("first", faulted);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Checkout(client, scenario)).StatusCode);
        Assert.Equal(0, (await Balance(scenario)).Reserved); Assert.Equal(10, (await Balance(scenario)).OnHand);
        Assert.Single((await Cart(customer)).Items);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.AnyAsync(x => x.Id == scenario.Request.OperationId, Ct));
            Assert.False(await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Reservations.AnyAsync(x => x.Id == scenario.Request.OperationId, Ct));
        }
        await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Failure_after_consuming_stock_rolls_back_confirmation_and_can_retry()
    {
        using var customer = fixture.Client("first"); using var staff = fixture.Client("staff");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        await using var faulted = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IOrderStock>(sp => new FailingStock(new OrderStock(sp.GetRequiredService<InventoryService>(),
                sp.GetRequiredService<InventoryDbContext>()), failReserve: false))));
        using var client = fixture.Client("staff", faulted);
        var path = $"/api/v1/admin/orders/{order.Id}/confirm";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync(path, null, Ct)).StatusCode);
        Assert.Equal(10, (await Balance(scenario)).OnHand); Assert.Equal(2, (await Balance(scenario)).Reserved);
        Assert.Equal("Pending", (await customer.GetFromJsonAsync<OrderResponse>(Api + "/" + order.Id, Ct))!.Status);
        await Read(await staff.PostAsync(path, null, Ct), HttpStatusCode.OK);
        Assert.Equal(8, (await Balance(scenario)).OnHand);
    }

    [Fact]
    public async Task Checkout_validates_authentication_body_payment_method_and_required_version()
    {
        using var customer = fixture.Client("first"); using var anonymous = fixture.Client();
        var scenario = await Prepare(customer);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Checkout(anonymous, scenario)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Checkout(customer, scenario with
            { Request = scenario.Request with { OperationId = Guid.Empty } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Checkout(customer, scenario with
            { Request = scenario.Request with { PaymentMethod = "Card" } })).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await customer.PostAsJsonAsync(Api + "/checkout", scenario.Request, Ct)).StatusCode);
        await ClearCart(customer);
        var empty = scenario with { Cart = await Cart(customer) };
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(customer, empty)).StatusCode);
    }

    [Fact]
    public async Task Insufficient_nearest_warehouse_falls_back_to_an_eligible_warehouse()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer, stock: 1);
        Guid fallback;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            fallback = Guid.NewGuid();
            db.Warehouses.Add(new Warehouse { Id = fallback, Code = Guid.NewGuid().ToString("N"), Name = "Fallback", Type = "Fixed",
                IsActive = true, Latitude = 30.051m, Longitude = 31.35m });
            db.Balances.Add(new StockBalance { WarehouseId = fallback, VariantId = scenario.Variant, OnHand = 10 });
            await db.SaveChangesAsync(Ct);
        }
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        Assert.Equal(fallback, order.WarehouseId);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
    }

    [Fact]
    public async Task Multi_item_checkout_requires_one_warehouse_with_all_items()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        Guid variantId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var source = await catalog.ProductVariants.AsNoTracking().SingleAsync(x => x.Id == scenario.Variant, Ct);
            var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = source.ProductId, Sku = Guid.NewGuid().ToString("N"),
                Name = "Another pack", Price = 10, IsActive = true };
            variantId = variant.Id; catalog.ProductVariants.Add(variant); await catalog.SaveChangesAsync(Ct);
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var warehouseId = Guid.NewGuid();
            inventory.Warehouses.Add(new Warehouse { Id = warehouseId, Code = Guid.NewGuid().ToString("N"), Name = "Other SKU only",
                Type = "Fixed", IsActive = true, Latitude = 30.05m, Longitude = 31.35m });
            inventory.Balances.Add(new StockBalance { WarehouseId = warehouseId, VariantId = variantId, OnHand = 10 });
            await inventory.SaveChangesAsync(Ct);
        }
        scenario = scenario with { Cart = await Set(customer, variantId, 2, scenario.Cart.Version),
            Request = scenario.Request with { ExpectedTotal = 60m } };
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(customer, scenario)).StatusCode);
        Assert.Equal(2, (await Cart(customer)).Items.Length);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
    }

    [Fact]
    public async Task Cancel_and_confirmation_race_reaches_one_terminal_state_with_consistent_stock()
    {
        using var customer = fixture.Client("first"); using var staff = fixture.Client("staff");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        var replies = await Task.WhenAll(customer.PostAsJsonAsync(Api + "/" + order.Id + "/cancel", new CancelOrderRequest("Cancel race"), Ct),
            staff.PostAsync($"/api/v1/admin/orders/{order.Id}/confirm", null, Ct));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        var result = (await customer.GetFromJsonAsync<OrderResponse>(Api + "/" + order.Id, Ct))!;
        var balance = await Balance(scenario);
        Assert.Equal(0, balance.Reserved);
        Assert.Equal(result.Status == "Confirmed" ? 8 : 10, balance.OnHand);
        Assert.Equal(2, result.History.Length);
    }

    [Fact]
    public async Task Disabled_checkout_does_not_modify_cart_or_inventory()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        await using var disabled = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Orders:EnableCheckout"] = "false" })));
        using var client = fixture.Client("first", disabled);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Checkout(client, scenario)).StatusCode);
        Assert.Single((await Cart(customer)).Items);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
    }

    [Fact]
    public async Task Failure_after_order_and_cart_save_rolls_back_every_module()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        await using var faulted = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IOrderTransaction>(sp => new FailingTransaction(new Velo.Api.Infrastructure.OrderSqlTransaction(
                sp.GetRequiredService<OrdersDbContext>(), sp.GetRequiredService<InventoryDbContext>(),
                sp.GetRequiredService<CustomersDbContext>(), sp.GetRequiredService<CatalogDbContext>(),
                sp.GetRequiredService<Velo.Modules.Payments.Infrastructure.Persistence.PaymentsDbContext>(),
                sp.GetRequiredService<Velo.Modules.Delivery.Infrastructure.Persistence.DeliveryDbContext>(),
                sp.GetRequiredService<Velo.Modules.Notifications.Infrastructure.Persistence.NotificationsDbContext>())))));
        using var client = fixture.Client("first", faulted);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Checkout(client, scenario)).StatusCode);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
        Assert.Single((await Cart(customer)).Items);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.AnyAsync(x => x.Id == scenario.Request.OperationId, Ct));
            Assert.False(await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Reservations.AnyAsync(x => x.Id == scenario.Request.OperationId, Ct));
            Assert.False(await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Movements.AnyAsync(x => x.OperationId == scenario.Request.OperationId, Ct));
        }
        await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
    }

    private sealed class FailingTransaction(Velo.Api.Infrastructure.OrderSqlTransaction inner) : IOrderTransaction, IAsyncDisposable
    {
        public Task<T> RunAsync<T>(Guid customer, Guid operation, Func<Task<T>> action, CancellationToken ct) =>
            inner.RunAsync<T>(customer, operation, async () =>
            { await action(); throw new InvalidOperationException("Injected failure after order and cart save"); }, ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Fact]
    public async Task Checkout_preview_shows_final_fee_and_total_without_creating_order_or_reserving_stock()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        var reply = await customer.PostAsJsonAsync(Api + "/checkout/preview",
            new CheckoutPreviewRequest(scenario.Address.Id, scenario.Address.Version), Ct);
        reply.EnsureSuccessStatusCode();
        var preview = (await reply.Content.ReadFromJsonAsync<CheckoutPreview>(Ct))!;
        Assert.Equal(20m, preview.DeliveryFee); Assert.Equal(40m, preview.Total);
        Assert.Equal(scenario.Cart.Version, preview.Cart.Version);
        Assert.Equal(scenario.Warehouse, preview.WarehouseId);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
        Assert.Single((await Cart(customer)).Items);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.AnyAsync(x => x.Id == scenario.Request.OperationId, Ct));
    }

    [Fact]
    public async Task Checkout_rejects_unaccepted_total_and_fee_change_without_modifying_stock()
    {
        using var customer = fixture.Client("first");
        var scenario = await Prepare(customer);
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(customer, scenario with
            { Request = scenario.Request with { ExpectedTotal = 0.01m } })).StatusCode);
        await using var changed = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Orders:DeliveryFee"] = "25" })));
        using var client = fixture.Client("first", changed);
        Assert.Equal(HttpStatusCode.Conflict, (await Checkout(client, scenario)).StatusCode);
        Assert.Equal(0, (await Balance(scenario)).Reserved);
        Assert.Single((await Cart(customer)).Items);
    }

    [Fact]
    public async Task Staff_can_read_order_queue_and_details_but_customer_cannot()
    {
        using var customer = fixture.Client("first"); using var staff = fixture.Client("staff");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/orders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/orders/" + order.Id, Ct)).StatusCode);
        var page = (await staff.GetFromJsonAsync<OrderPage>("/api/v1/admin/orders?status=Pending&pageSize=100", Ct))!;
        Assert.Contains(page.Items, x => x.Id == order.Id);
        Assert.All(page.Items, x => Assert.Equal("Pending", x.Status));
        Assert.Equal(order.Id, (await staff.GetFromJsonAsync<OrderResponse>("/api/v1/admin/orders/" + order.Id, Ct))!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/admin/orders?status=Invalid", Ct)).StatusCode);
    }

    private sealed record Scenario(Guid Variant, Guid Warehouse, AddressResponse Address, CartResponse Cart, CheckoutRequest Request);
    private async Task<Scenario> Prepare(HttpClient client, int quantity = 2, int stock = 10)
    {
        await ClearCart(client);
        var address = await Address(client);
        using var scope = fixture.Factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = "Orders " + suffix, NormalizedName = suffix.ToUpperInvariant(), IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), Name = "Bread", Category = category, IsActive = true };
        var variant = new ProductVariant { Id = Guid.NewGuid(), Name = "Pack", Sku = suffix, Product = product, Price = 10, IsActive = true };
        catalog.ProductVariants.Add(variant); await catalog.SaveChangesAsync(Ct);
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix, Name = "Orders test", Type = "Fixed",
            IsActive = true, Latitude = 30.05m, Longitude = 31.35m };
        inventory.Warehouses.Add(warehouse);
        inventory.Balances.Add(new StockBalance { WarehouseId = warehouse.Id, VariantId = variant.Id, OnHand = stock });
        await inventory.SaveChangesAsync(Ct);
        var cart = await Set(client, variant.Id, quantity, (await Cart(client)).Version);
        return new(variant.Id, warehouse.Id, address, cart, new CheckoutRequest(Guid.NewGuid(), address.Id, address.Version, 10m * quantity + 20m));
    }
    private static async Task<AddressResponse> Address(HttpClient client)
    {
        // Keep this shared test user below the application's saved-address limit.
        var existing = (await client.GetFromJsonAsync<AddressResponse[]>(Addresses, Ct))!;
        foreach (var item in existing) (await client.DeleteAsync(Addresses + "/" + item.Id, Ct)).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync(Addresses, new AddressRequest("Home", "Test Customer", "+201012345678",
            "Cairo", "Nasr City", "Test street", "10", 30.05m, 31.35m), Ct);
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AddressResponse>(Ct))!;
    }
    private static Task<HttpResponseMessage> Checkout(HttpClient client, Scenario scenario)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Api + "/checkout") { Content = JsonContent.Create(scenario.Request) };
        request.Headers.TryAddWithoutValidation("If-Match", '"' + scenario.Cart.Version + '"');
        return client.SendAsync(request, Ct);
    }
    private static async Task<OrderResponse> Read(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode); return (await response.Content.ReadFromJsonAsync<OrderResponse>(Ct))!;
    }
    private static async Task<CartResponse> Cart(HttpClient client) => (await client.GetFromJsonAsync<CartResponse>("/api/v1/cart", Ct))!;
    private static async Task<CartResponse> Set(HttpClient client, Guid variant, int quantity, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/cart/items/" + variant) { Content = JsonContent.Create(new SetCartItemRequest(quantity)) };
        request.Headers.TryAddWithoutValidation("If-Match", '"' + version + '"');
        var response = await client.SendAsync(request, Ct); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>(Ct))!;
    }
    private static async Task ClearCart(HttpClient client)
    {
        var cart = await Cart(client);
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cart");
        request.Headers.TryAddWithoutValidation("If-Match", '"' + cart.Version + '"');
        (await client.SendAsync(request, Ct)).EnsureSuccessStatusCode();
    }
    private async Task<StockBalance> Balance(Scenario scenario)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Balances.AsNoTracking()
            .SingleAsync(x => x.WarehouseId == scenario.Warehouse && x.VariantId == scenario.Variant, Ct);
    }
    private sealed class FailingStock(IOrderStock inner, bool failReserve) : IOrderStock
    {
        public Task RestoreAsync(Guid order, Guid warehouse, StockItem[] items, Guid actor, string reason, CancellationToken ct) =>
            inner.RestoreAsync(order, warehouse, items, actor, reason, ct);
        public Task<Guid?> FindWarehouseAsync(decimal lat, decimal lon, double radius, StockItem[] items, CancellationToken ct) =>
            inner.FindWarehouseAsync(lat, lon, radius, items, ct);
        public async Task<OrderReservation> ReserveAsync(Guid op, Guid user, Guid warehouse, StockItem[] items, int minutes, CancellationToken ct)
        {
            var result = await inner.ReserveAsync(op, user, warehouse, items, minutes, ct);
            if (failReserve) throw new InvalidOperationException("Injected failure after reserve");
            return result;
        }
        public async Task<OrderReservation> FinishAsync(Guid reservation, bool commit, Guid actor, CancellationToken ct)
        {
            var result = await inner.FinishAsync(reservation, commit, actor, ct);
            if (!failReserve && commit) throw new InvalidOperationException("Injected failure after commit");
            return result;
        }
    }
}
