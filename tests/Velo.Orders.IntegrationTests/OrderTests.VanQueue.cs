using System.Net;
using System.Net.Http.Json;
using Velo.Modules.Orders.Application;
using Xunit;

namespace Velo.Orders.IntegrationTests;

public sealed partial class OrderTests
{
    [Fact]
    public async Task Van_queue_filters_by_warehouse_before_counting_and_pagination()
    {
        using var customer = fixture.Client("first");
        using var staff = fixture.Client("staff");
        var scenario = await Prepare(customer);
        var order = await Read(await Checkout(customer, scenario), HttpStatusCode.Created);
        var ownQueue = (await staff.GetFromJsonAsync<OrderPage>(
            $"/api/v1/admin/orders?warehouseId={scenario.Warehouse}&status=Pending&pageSize=100", Ct))!;
        Assert.Contains(ownQueue.Items, x => x.Id == order.Id);
        Assert.All(ownQueue.Items, x => Assert.Equal(scenario.Warehouse, x.WarehouseId));
        var otherQueue = (await staff.GetFromJsonAsync<OrderPage>(
            $"/api/v1/admin/orders?warehouseId={Guid.NewGuid()}&status=Pending&pageSize=1", Ct))!;
        Assert.Empty(otherQueue.Items);
        Assert.Equal(0, otherQueue.TotalCount);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(
            $"/api/v1/admin/orders?warehouseId={scenario.Warehouse}", Ct)).StatusCode);
    }
}
