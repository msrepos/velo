using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Catalog.Domain;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Infrastructure;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Xunit;
namespace Velo.Inventory.IntegrationTests;

public sealed class InventoryTests(InventoryFixture fixture) : IClassFixture<InventoryFixture>
{
    private const string Api = "/api/v1/admin/inventory";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string value) => value + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Inventory_requires_its_own_management_permission()
    {
        using var anonymous = fixture.Client();
        using var customer = fixture.Client("customer");
        using var catalog = fixture.Client("catalog");
        using var staff = fixture.Client("staff");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Api + "/warehouses", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Api + "/warehouses", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalog.GetAsync(Api + "/warehouses", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Api + "/warehouses", Ct)).StatusCode);
    }

    [Fact]
    public async Task Warehouses_validate_codes_coordinates_and_concurrent_edits()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        Assert.StartsWith("WH", warehouse.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Api + "/warehouses",
            new WarehouseRequest(warehouse.Code.ToLowerInvariant(), "Duplicate"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Api + "/warehouses",
            new WarehouseRequest("bad code", "Bad"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Api + "/warehouses",
            new WarehouseRequest(Unique("WH"), "Bad", Latitude: 91, Longitude: 31), Ct)).StatusCode);
        var change = new WarehouseRequest(warehouse.Code, "Mobile warehouse", "Mobile",
            Latitude: 30.123456m, Longitude: 31.123456m, Version: warehouse.Version);
        (await client.PutAsJsonAsync(Api + "/warehouses/" + warehouse.Id, change, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Api + "/warehouses/" + warehouse.Id,
            change with { Name = "Stale edit" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Adjustment_retries_are_idempotent_and_changed_payload_is_rejected()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var variant = await Variant();
        var request = new AdjustmentRequest(Guid.NewGuid(), variant, 10, "Opening stock");
        var first = await Adjustment(client, warehouse.Id, request);
        var repeated = await Adjustment(client, warehouse.Id, request);
        Assert.Equal(first.MovementId, repeated.MovementId);
        Assert.Equal(10, (await Balance(client, warehouse.Id, variant)).OnHand);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Api}/warehouses/{warehouse.Id}/adjustments",
            request with { QuantityDelta = 20 }, Ct)).StatusCode);
        var ledger = (await client.GetFromJsonAsync<InventoryPage<MovementResponse>>(
            $"{Api}/warehouses/{warehouse.Id}/movements", Ct))!;
        var movement = Assert.Single(ledger.Items);
        Assert.Equal(10, movement.OnHandDelta);
        Assert.NotNull(movement.ActorUserId);
        Assert.Equal(10, movement.OnHandAfter);
    }

    [Fact]
    public async Task Concurrent_retries_add_stock_once()
    {
        using var first = fixture.Client("staff");
        using var second = fixture.Client("staff");
        var warehouse = await Warehouse(first);
        var variant = await Variant();
        var body = new AdjustmentRequest(Guid.NewGuid(), variant, 10, "Concurrent delivery retry");
        var path = $"{Api}/warehouses/{warehouse.Id}/adjustments";
        var replies = await Task.WhenAll(first.PostAsJsonAsync(path, body, Ct), second.PostAsJsonAsync(path, body, Ct));
        Assert.All(replies, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var a = (await replies[0].Content.ReadFromJsonAsync<AdjustmentResponse>(Ct))!;
        var b = (await replies[1].Content.ReadFromJsonAsync<AdjustmentResponse>(Ct))!;
        Assert.Equal(a.MovementId, b.MovementId);
        Assert.Equal(10, (await Balance(first, warehouse.Id, variant)).OnHand);
    }

    [Fact]
    public async Task Transfers_are_atomic_idempotent_and_cannot_overdraw()
    {
        using var client = fixture.Client("staff");
        var source = await Warehouse(client);
        var target = await Warehouse(client);
        var variant = await Variant();
        await Add(client, source.Id, variant, 10);
        var body = new TransferRequest(Guid.NewGuid(), source.Id, target.Id, variant, 3, "Restocking");
        (await client.PostAsJsonAsync(Api + "/transfers", body, Ct)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Api + "/transfers", body, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(7, (await Balance(client, source.Id, variant)).OnHand);
        Assert.Equal(3, (await Balance(client, target.Id, variant)).OnHand);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Api + "/transfers",
            body with { OperationId = Guid.NewGuid(), Quantity = 8 }, Ct)).StatusCode);
        Assert.Equal(3, (await Balance(client, target.Id, variant)).OnHand);
    }

    [Fact]
    public async Task Commit_deducts_on_hand_and_reserved_exactly_once()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var variant = await Variant();
        await Add(client, warehouse.Id, variant, 5);
        var reservation = await Reserve(client, warehouse.Id, [new(variant, 3)]);
        Assert.Equal(new BalanceResponse(warehouse.Id, variant, 5, 3, 2), await Balance(client, warehouse.Id, variant));
        var path = $"{Api}/reservations/{reservation.Id}/commit";
        (await client.PostAsync(path, null, Ct)).EnsureSuccessStatusCode();
        (await client.PostAsync(path, null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(new BalanceResponse(warehouse.Id, variant, 2, 0, 2), await Balance(client, warehouse.Id, variant));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"{Api}/reservations/{reservation.Id}/release", null, Ct)).StatusCode);
        var ledger = (await client.GetFromJsonAsync<InventoryPage<MovementResponse>>(
            $"{Api}/warehouses/{warehouse.Id}/movements", Ct))!;
        Assert.Single(ledger.Items, x => x.Kind == "Commit");
    }

    [Fact]
    public async Task Releasing_is_idempotent_and_does_not_reduce_physical_stock()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var variant = await Variant();
        await Add(client, warehouse.Id, variant, 10);
        var reservation = await Reserve(client, warehouse.Id, [new(variant, 4)]);
        var path = $"{Api}/reservations/{reservation.Id}/release";
        (await client.PostAsync(path, null, Ct)).EnsureSuccessStatusCode();
        (await client.PostAsync(path, null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(new BalanceResponse(warehouse.Id, variant, 10, 0, 10), await Balance(client, warehouse.Id, variant));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"{Api}/reservations/{reservation.Id}/commit", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Two_customers_cannot_reserve_the_last_unit()
    {
        using var a = fixture.Client("staff");
        using var b = fixture.Client("staff");
        var warehouse = await Warehouse(a);
        var variant = await Variant();
        await Add(a, warehouse.Id, variant, 1);
        var one = new ReservationRequest(Guid.NewGuid(), warehouse.Id, Unique("Order"), [new(variant, 1)]);
        var two = one with { OperationId = Guid.NewGuid(), Reference = Unique("Order") };
        var responses = await Task.WhenAll(a.PostAsJsonAsync(Api + "/reservations", one, Ct),
            b.PostAsJsonAsync(Api + "/reservations", two, Ct));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(new BalanceResponse(warehouse.Id, variant, 1, 1, 0), await Balance(a, warehouse.Id, variant));
    }

    [Fact]
    public async Task Multi_item_reservations_roll_back_as_a_unit_and_can_retry_after_stock_arrives()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var first = await Variant();
        var second = await Variant();
        await Add(client, warehouse.Id, first, 5);
        var body = new ReservationRequest(Guid.NewGuid(), warehouse.Id, Unique("Order"), [new(first, 2), new(second, 1)]);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Api + "/reservations", body, Ct)).StatusCode);
        Assert.Equal(0, (await Balance(client, warehouse.Id, first)).Reserved);
        await Add(client, warehouse.Id, second, 1);
        var accepted = await client.PostAsJsonAsync(Api + "/reservations", body, Ct);
        accepted.EnsureSuccessStatusCode();
        var repeated = await client.PostAsJsonAsync(Api + "/reservations", body with { Items = body.Items.Reverse().ToArray() }, Ct);
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(2, (await Balance(client, warehouse.Id, first)).Reserved);
        Assert.Equal(1, (await Balance(client, warehouse.Id, second)).Reserved);
    }

    [Fact]
    public async Task Expiry_releases_stock_and_prevents_commit()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var variant = await Variant();
        await Add(client, warehouse.Id, variant, 2);
        var reservation = await Reserve(client, warehouse.Id, [new(variant, 2)], 1);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(2, (await Balance(client, warehouse.Id, variant)).Available);
        var state = (await client.GetFromJsonAsync<ReservationResponse>($"{Api}/reservations/{reservation.Id}", Ct))!;
        Assert.Equal("Expired", state.Status);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync($"{Api}/reservations/{reservation.Id}/commit", null, Ct)).StatusCode);
        (await client.PostAsync($"{Api}/reservations/{reservation.Id}/release", null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await Balance(client, warehouse.Id, variant)).Reserved);
    }

    [Fact]
    public async Task Expiry_pass_is_repeatable_across_workers()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var variant = await Variant();
        await Add(client, warehouse.Id, variant, 1);
        var reservation = await Reserve(client, warehouse.Id, [new(variant, 1)], 1);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        using var firstScope = fixture.Factory.Services.CreateScope();
        using var secondScope = fixture.Factory.Services.CreateScope();
        await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<InventoryService>().ExpireDueAsync(null, 100, Ct),
            secondScope.ServiceProvider.GetRequiredService<InventoryService>().ExpireDueAsync(null, 100, Ct));
        Assert.Equal(1, (await Balance(client, warehouse.Id, variant)).Available);
        var ledger = (await client.GetFromJsonAsync<InventoryPage<MovementResponse>>(
            $"{Api}/warehouses/{warehouse.Id}/movements", Ct))!;
        var expired = Assert.Single(ledger.Items, x => x.Kind == "Expire");
        Assert.Null(expired.ActorUserId);
    }

    [Fact]
    public async Task Reserved_stock_cannot_be_adjusted_or_transferred_out()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var target = await Warehouse(client);
        var variant = await Variant();
        await Add(client, warehouse.Id, variant, 5);
        await Reserve(client, warehouse.Id, [new(variant, 4)]);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Api}/warehouses/{warehouse.Id}/adjustments",
            new AdjustmentRequest(Guid.NewGuid(), variant, -2, "Would consume reserved stock"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Api + "/transfers",
            new TransferRequest(Guid.NewGuid(), warehouse.Id, target.Id, variant, 2, "Would move reserved stock"), Ct)).StatusCode);
        Assert.Equal(5, (await Balance(client, warehouse.Id, variant)).OnHand);
    }

    [Fact]
    public async Task References_are_unique_and_inactive_warehouses_still_allow_finishing_existing_work()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        var variant = await Variant();
        await Add(client, warehouse.Id, variant, 5);
        var body = new ReservationRequest(Guid.NewGuid(), warehouse.Id, Unique("Order"), [new(variant, 1)]);
        (await client.PostAsJsonAsync(Api + "/reservations", body, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Api + "/reservations",
            body with { OperationId = Guid.NewGuid() }, Ct)).StatusCode);
        (await client.PutAsJsonAsync(Api + "/warehouses/" + warehouse.Id,
            new WarehouseRequest(warehouse.Code, warehouse.Name, IsActive: false, Version: warehouse.Version), Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Api}/warehouses/{warehouse.Id}/adjustments",
            new AdjustmentRequest(Guid.NewGuid(), variant, 1, "Closed"), Ct)).StatusCode);
        (await client.PostAsync($"{Api}/reservations/{body.OperationId}/commit", null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(4, (await Balance(client, warehouse.Id, variant)).OnHand);
    }

    [Fact]
    public async Task Catalog_contract_rejects_unknown_and_unpublished_variants()
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Api}/warehouses/{warehouse.Id}/adjustments",
            new AdjustmentRequest(Guid.NewGuid(), Guid.NewGuid(), 1, "Unknown SKU"), Ct)).StatusCode);
        var hidden = await Variant(false);
        await Add(client, warehouse.Id, hidden, 5); // Staff may count inactive stock.
        var request = new ReservationRequest(Guid.NewGuid(), warehouse.Id, Unique("Order"), [new(hidden, 1)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Api + "/reservations", request, Ct)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000001)]
    [InlineData(-1000001)]
    public async Task Invalid_adjustments_are_rejected(int quantity)
    {
        using var client = fixture.Client("staff");
        var warehouse = await Warehouse(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Api}/warehouses/{warehouse.Id}/adjustments",
            new AdjustmentRequest(Guid.NewGuid(), Guid.NewGuid(), quantity, "Invalid"), Ct)).StatusCode);
    }

    private static async Task<WarehouseResponse> Warehouse(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Api + "/warehouses", new WarehouseRequest(Unique("wh"), "Test warehouse"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>(Ct))!;
    }
    private async Task<Guid> Variant(bool active = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Name = Unique("Category"), NormalizedName = Unique("CAT"), IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Test product", IsActive = true };
        var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, Sku = Unique("SKU"), Name = "One pack", Price = 10, IsActive = active };
        db.AddRange(category, product, variant);
        await db.SaveChangesAsync(Ct);
        return variant.Id;
    }
    private static async Task<AdjustmentResponse> Adjustment(HttpClient client, Guid warehouse, AdjustmentRequest request)
    {
        var response = await client.PostAsJsonAsync($"{Api}/warehouses/{warehouse}/adjustments", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdjustmentResponse>(Ct))!;
    }
    private static Task<AdjustmentResponse> Add(HttpClient client, Guid warehouse, Guid variant, int quantity) =>
        Adjustment(client, warehouse, new(Guid.NewGuid(), variant, quantity, "Test receipt"));
    private static async Task<BalanceResponse> Balance(HttpClient client, Guid warehouse, Guid variant) =>
        (await client.GetFromJsonAsync<BalanceResponse>($"{Api}/warehouses/{warehouse}/balances/{variant}", Ct))!;
    private static async Task<ReservationResponse> Reserve(HttpClient client, Guid warehouse, ReservationItem[] items, int minutes = 10)
    {
        var response = await client.PostAsJsonAsync(Api + "/reservations",
            new ReservationRequest(Guid.NewGuid(), warehouse, Unique("Order"), items, minutes), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReservationResponse>(Ct))!;
    }
}
