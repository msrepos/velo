using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Notifications.Infrastructure.Persistence;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Velo.Modules.Payments.Application;
using Velo.Modules.Payments.Contracts;
using Velo.Modules.Payments.Infrastructure.Persistence;
using Velo.Modules.Customers.Infrastructure.Persistence;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Xunit;
namespace Velo.Orders.IntegrationTests;
public sealed partial class OrderTests
{
    [Fact]
    public async Task Full_delivery_collects_exact_cash_once_and_handover_settles_once()
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver");
        var baseline = (await driver.GetFromJsonAsync<CashBalanceResponse>("/api/v1/driver/cash", Ct))!;
        var scenario = await Prepare(customer); var order = await ToRoad(customer, ops, driver, profile.Id, scenario);
        var code = (await customer.GetFromJsonAsync<DeliveryCodeResponse>(Api + "/" + order.Id + "/delivery-code", Ct))!;
        var body = new CompleteDeliveryRequest(Guid.NewGuid(), code.Code, order.Total);
        var delivered = await Read(await driver.PostAsJsonAsync(CompletePath(order.Id), body, Ct), HttpStatusCode.OK);
        Assert.Equal("Delivered", delivered.Status); Assert.Equal("Paid", delivered.PaymentStatus);
        await Read(await driver.PostAsJsonAsync(CompletePath(order.Id), body, Ct), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await driver.PostAsJsonAsync(CompletePath(order.Id), body with { OperationId = Guid.NewGuid() }, Ct)).StatusCode);
        Assert.Equal(baseline.OutstandingCash + order.Total, (await driver.GetFromJsonAsync<CashBalanceResponse>("/api/v1/driver/cash", Ct))!.OutstandingCash);
        var receipt = (await customer.GetFromJsonAsync<PaymentReceipt>("/api/v1/payments/" + order.Id, Ct))!;
        Assert.Equal("Collected", receipt.Status); Assert.Equal(body.OperationId, receipt.CollectionOperationId);
        var settlement = new SettlementRequest(Guid.NewGuid(), profile.Id, [order.Id], order.Total, "cash-" + Guid.NewGuid().ToString("N"));
        (await ops.PostAsJsonAsync("/api/v1/admin/payments/settlements", settlement, Ct)).EnsureSuccessStatusCode();
        (await ops.PostAsJsonAsync("/api/v1/admin/payments/settlements", settlement, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(baseline.OutstandingCash, (await driver.GetFromJsonAsync<CashBalanceResponse>("/api/v1/driver/cash", Ct))!.OutstandingCash);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync("/api/v1/admin/payments/settlements", settlement with { OperationId = Guid.NewGuid() }, Ct)).StatusCode);
        Assert.Equal(8, (await Balance(scenario)).OnHand);
        Assert.Null((await driver.GetFromJsonAsync<DriverDeliveryPage>("/api/v1/driver/deliveries?pageSize=100", Ct))!.Items.Single(x => x.OrderId == order.Id).Dropoff);
    }
    [Fact]
    public async Task Assignment_ownership_and_customer_only_proof_protect_delivery_and_tracking()
    {
        using var customer = fixture.Client("first"); using var other = fixture.Client("second"); using var ops = fixture.Client("ops");
        using var driver = fixture.Client("driver"); using var stranger = fixture.Client("otherDriver");
        var profile = await Driver(ops, driver, "driver"); await Driver(ops, stranger, "otherDriver");
        var order = await ToRoad(customer, ops, driver, profile.Id, await Prepare(customer));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync(DispatchPath(order.Id), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(CompletePath(order.Id), new CompleteDeliveryRequest(Guid.NewGuid(), "123456", order.Total), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await driver.GetAsync(Api + "/" + order.Id + "/delivery-code", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Api + "/" + order.Id + "/tracking", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/v1/payments/" + order.Id, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/driver/deliveries", Ct)).StatusCode);
    }
    [Fact]
    public async Task Failed_codes_commit_attempt_limits_and_customer_can_rotate_locked_proof()
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver"); var order = await ToRoad(customer, ops, driver, profile.Id, await Prepare(customer));
        var proof = (await customer.GetFromJsonAsync<DeliveryCodeResponse>(Api + "/" + order.Id + "/delivery-code", Ct))!;
        var wrong = proof.Code == "000000" ? "111111" : "000000";
        Assert.Equal(HttpStatusCode.Conflict, (await driver.PostAsJsonAsync(CompletePath(order.Id), new CompleteDeliveryRequest(Guid.NewGuid(), proof.Code, order.Total - 1), Ct)).StatusCode);
        for(var i = 0; i < 5; i++) Assert.Equal(i == 4 ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadRequest,
            (await driver.PostAsJsonAsync(CompletePath(order.Id), new CompleteDeliveryRequest(Guid.NewGuid(), wrong, order.Total), Ct)).StatusCode);
        using(var scope = fixture.Factory.Services.CreateScope())
        {
            Assert.Equal(5, (await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Shipments.SingleAsync(x => x.OrderId == order.Id, Ct)).ProofAttempts);
            Assert.Equal("Pending", (await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.SingleAsync(x => x.OrderId == order.Id, Ct)).Status);
        }
        var fresh = (await customer.GetFromJsonAsync<DeliveryCodeResponse>(Api + "/" + order.Id + "/delivery-code", Ct))!;
        await Read(await driver.PostAsJsonAsync(CompletePath(order.Id), new CompleteDeliveryRequest(Guid.NewGuid(), fresh.Code, order.Total), Ct), HttpStatusCode.OK);
    }
    [Fact]
    public async Task Tracking_only_shows_fresh_location_and_updates_are_throttled()
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver"); var order = await ToRoad(customer, ops, driver, profile.Id, await Prepare(customer));
        Assert.Null((await customer.GetFromJsonAsync<TrackingResponse>(Api + "/" + order.Id + "/tracking", Ct))!.Latitude);
        Assert.Equal(HttpStatusCode.NoContent, (await driver.PostAsJsonAsync("/api/v1/driver/location", new LocationRequest(30.051m, 31.351m), Ct)).StatusCode);
        Assert.Equal(30.051m, (await customer.GetFromJsonAsync<TrackingResponse>(Api + "/" + order.Id + "/tracking", Ct))!.Latitude);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await driver.PostAsJsonAsync("/api/v1/driver/location", new LocationRequest(30.052m, 31.351m), Ct)).StatusCode);
        using(var scope = fixture.Factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Drivers.Where(x => x.Id == profile.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationAt, fixture.Clock.GetUtcNow().AddMinutes(-6)), Ct);
        Assert.Null((await customer.GetFromJsonAsync<TrackingResponse>(Api + "/" + order.Id + "/tracking", Ct))!.Latitude);
    }
    [Fact]
    public async Task Driver_capacity_and_cancellation_restore_stock_without_duplicate_compensation()
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver"); var first = await Prepare(customer); var one = await ToAssigned(customer, ops, profile.Id, first);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PutAsJsonAsync("/api/v1/admin/delivery/drivers/" + profile.Id, new UpdateDriverRequest(profile.Name, false, profile.Version), Ct)).StatusCode);
        var second = await Prepare(customer); var two = await ToReady(customer, ops, second);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Admin(two.Id, "assign"), new AssignDriverRequest(profile.Id), Ct)).StatusCode);
        var cancel = new AdminCancelRequest("Cancel before departure");
        await Read(await ops.PostAsJsonAsync(Admin(one.Id, "cancel"), cancel, Ct), HttpStatusCode.OK);
        await Read(await ops.PostAsJsonAsync(Admin(one.Id, "cancel"), cancel, Ct), HttpStatusCode.OK);
        Assert.Equal(10, (await Balance(first)).OnHand);
        await Read(await ops.PostAsJsonAsync(Admin(two.Id, "assign"), new AssignDriverRequest(profile.Id), Ct), HttpStatusCode.OK);
    }
    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 8)]
    public async Task Failed_delivery_requires_explicit_restock_and_changed_return_intent_is_rejected(bool restock, int expected)
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver"); var scenario = await Prepare(customer); var order = await ToRoad(customer, ops, driver, profile.Id, scenario);
        await Read(await driver.PostAsJsonAsync(FailPath(order.Id), new DeliveryFailureRequest("Customer unavailable"), Ct), HttpStatusCode.OK);
        var request = new ReturnOrderRequest("Returned and inspected", restock);
        var returned = await Read(await ops.PostAsJsonAsync(Admin(order.Id, "return-to-warehouse"), request, Ct), HttpStatusCode.OK);
        Assert.Equal("Returned", returned.Status); Assert.Equal("Cancelled", returned.PaymentStatus);
        await Read(await ops.PostAsJsonAsync(Admin(order.Id, "return-to-warehouse"), request, Ct), HttpStatusCode.OK);
        Assert.Equal(expected, (await Balance(scenario)).OnHand);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Admin(order.Id, "return-to-warehouse"), request with { Restock = !restock }, Ct)).StatusCode);
    }
    [Fact]
    public async Task Failed_delivery_can_be_assigned_for_another_attempt_without_deducting_stock_again()
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver"); var scenario = await Prepare(customer); var order = await ToRoad(customer, ops, driver, profile.Id, scenario);
        await Read(await driver.PostAsJsonAsync(FailPath(order.Id), new DeliveryFailureRequest("First attempt"), Ct), HttpStatusCode.OK);
        await Read(await ops.PostAsJsonAsync(Admin(order.Id, "assign"), new AssignDriverRequest(profile.Id), Ct), HttpStatusCode.OK);
        await Read(await driver.PostAsync(DispatchPath(order.Id), null, Ct), HttpStatusCode.OK);
        Assert.Equal(8, (await Balance(scenario)).OnHand);
    }
    [Fact]
    public async Task In_app_notifications_are_deduplicated_and_only_recipient_can_mark_read()
    {
        using var customer = fixture.Client("first"); using var other = fixture.Client("second"); using var ops = fixture.Client("ops");
        var scenario = await Prepare(customer); var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        await Read(await Checkout(customer, scenario), HttpStatusCode.OK);
        await Read(await ops.PostAsync(Admin(order.Id, "confirm"), null, Ct), HttpStatusCode.OK);
        await Read(await ops.PostAsync(Admin(order.Id, "confirm"), null, Ct), HttpStatusCode.OK);
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var messages = await db.Messages.AsNoTracking().Where(x => x.DeduplicationKey.StartsWith("order:" + order.Id.ToString("N") + ":")).ToArrayAsync(Ct);
        Assert.Equal(2, messages.Length); var id = messages[0].Id;
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync("/api/v1/notifications/" + id + "/read", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await customer.PostAsync("/api/v1/notifications/" + id + "/read", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await customer.PostAsync("/api/v1/notifications/" + id + "/read", null, Ct)).StatusCode);
        Assert.NotNull((await db.Messages.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).ReadAt);
    }
    [Fact]
    public async Task Failed_transaction_rolls_back_delivery_payment_and_notification_together()
    {
        using var customer = fixture.Client("first"); using var ops = fixture.Client("ops"); using var driver = fixture.Client("driver");
        var profile = await Driver(ops, driver, "driver"); var order = await ToRoad(customer, ops, driver, profile.Id, await Prepare(customer));
        var code = (await customer.GetFromJsonAsync<DeliveryCodeResponse>(Api + "/" + order.Id + "/delivery-code", Ct))!;
        var request = new CompleteDeliveryRequest(Guid.NewGuid(), code.Code, order.Total);
        await using var faulted = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IOrderTransaction>(sp =>
            new FailingTransaction(new Velo.Api.Infrastructure.OrderSqlTransaction(sp.GetRequiredService<OrdersDbContext>(), sp.GetRequiredService<InventoryDbContext>(),
                sp.GetRequiredService<CustomersDbContext>(), sp.GetRequiredService<CatalogDbContext>(), sp.GetRequiredService<PaymentsDbContext>(),
                sp.GetRequiredService<DeliveryDbContext>(), sp.GetRequiredService<NotificationsDbContext>())))));
        using var broken = fixture.Client("driver", faulted);
        Assert.Equal(HttpStatusCode.InternalServerError, (await broken.PostAsJsonAsync(CompletePath(order.Id), request, Ct)).StatusCode);
        using(var scope = fixture.Factory.Services.CreateScope())
        {
            Assert.Equal("OutForDelivery", (await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Shipments.SingleAsync(x => x.OrderId == order.Id, Ct)).Status);
            Assert.Equal("Pending", (await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.SingleAsync(x => x.OrderId == order.Id, Ct)).Status);
            Assert.Equal("OutForDelivery", (await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.SingleAsync(x => x.Id == order.Id, Ct)).Status);
            Assert.False(await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Messages.AnyAsync(x => x.Kind == "Order.Delivered" && x.DeduplicationKey.StartsWith("order:" + order.Id.ToString("N") + ":"), Ct));
        }
        await Read(await driver.PostAsJsonAsync(CompletePath(order.Id), request, Ct), HttpStatusCode.OK);
    }
    [Fact]
    public async Task New_management_routes_require_their_own_permissions()
    {
        using var customer = fixture.Client("first"); using var ordersOnly = fixture.Client("staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await ordersOnly.GetAsync("/api/v1/admin/payments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ordersOnly.GetAsync("/api/v1/admin/delivery/drivers", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/driver/cash", Ct)).StatusCode);
    }
    private static string Admin(Guid id, string action) => "/api/v1/admin/orders/" + id + "/" + action;
    private static string DispatchPath(Guid id) => "/api/v1/driver/deliveries/" + id + "/dispatch";
    private static string CompletePath(Guid id) => "/api/v1/driver/deliveries/" + id + "/complete";
    private static string FailPath(Guid id) => "/api/v1/driver/deliveries/" + id + "/fail";
    private async Task<DriverResponse> Driver(HttpClient ops, HttpClient driver, string name)
    {
        var list = (await ops.GetFromJsonAsync<DriverPage>("/api/v1/admin/delivery/drivers?pageSize=100", Ct))!;
        var profile = list.Items.SingleOrDefault(x => x.UserId == fixture.UserIds[name]);
        if(profile is null)
        {
            var response = await ops.PostAsJsonAsync("/api/v1/admin/delivery/drivers", new CreateDriverRequest(fixture.UserIds[name], name), Ct);
            response.EnsureSuccessStatusCode(); profile = (await response.Content.ReadFromJsonAsync<DriverResponse>(Ct))!;
        }
        foreach(var job in (await driver.GetFromJsonAsync<DriverDeliveryPage>("/api/v1/driver/deliveries?pageSize=100", Ct))!.Items)
        {
            if(job.Status == "Assigned") await Read(await ops.PostAsJsonAsync(Admin(job.OrderId, "cancel"), new AdminCancelRequest("Test cleanup"), Ct), HttpStatusCode.OK);
            else if(job.Status is "OutForDelivery" or "Failed")
            {
                if(job.Status == "OutForDelivery") await Read(await driver.PostAsJsonAsync(FailPath(job.OrderId), new DeliveryFailureRequest("Test cleanup"), Ct), HttpStatusCode.OK);
                await Read(await ops.PostAsJsonAsync(Admin(job.OrderId, "return-to-warehouse"), new ReturnOrderRequest("Test cleanup", false), Ct), HttpStatusCode.OK);
            }
        }
        using(var scope = fixture.Factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Drivers.Where(x => x.Id == profile.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationAt, (DateTimeOffset?)null), Ct);
        return (await ops.GetFromJsonAsync<DriverPage>("/api/v1/admin/delivery/drivers?pageSize=100", Ct))!.Items.Single(x => x.Id == profile.Id);
    }
    private static async Task<OrderResponse> ToReady(HttpClient customer, HttpClient ops, Scenario scenario)
    {
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        await Read(await ops.PostAsync(Admin(order.Id, "confirm"), null, Ct), HttpStatusCode.OK);
        await Read(await ops.PostAsync(Admin(order.Id, "prepare"), null, Ct), HttpStatusCode.OK);
        return await Read(await ops.PostAsync(Admin(order.Id, "ready"), null, Ct), HttpStatusCode.OK);
    }
    private static async Task<OrderResponse> ToAssigned(HttpClient customer, HttpClient ops, Guid id, Scenario scenario)
    { var order = await ToReady(customer, ops, scenario); return await Read(await ops.PostAsJsonAsync(Admin(order.Id, "assign"), new AssignDriverRequest(id), Ct), HttpStatusCode.OK); }
    private static async Task<OrderResponse> ToRoad(HttpClient customer, HttpClient ops, HttpClient driver, Guid id, Scenario scenario)
    { var order = await ToAssigned(customer, ops, id, scenario); return await Read(await driver.PostAsync(DispatchPath(order.Id), null, Ct), HttpStatusCode.OK); }
}
