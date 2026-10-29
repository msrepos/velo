using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Domain;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;
using Velo.Modules.Inventory.Application;
using Xunit;

namespace Velo.Orders.IntegrationTests;

public sealed partial class OrderTests
{
    private const string Fleet = "/api/v1/admin/delivery";

    [Fact]
    public async Task Fleet_registration_requires_management_and_verified_unique_accounts()
    {
        using var ops = fixture.Client("ops"); using var customer = fixture.Client("first");
        var account = await FleetAccount();
        var request = new CreateCrewRequest(account.Id, "Prep member");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Fleet + "/crew", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync(Fleet + "/crew", request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ops.PostAsJsonAsync(Fleet + "/crew", request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Fleet + "/crew", request, Ct)).StatusCode);
        var unverified = await FleetAccount(false);
        Assert.Equal(HttpStatusCode.BadRequest, (await ops.PostAsJsonAsync(Fleet + "/crew", request with { UserId = unverified.Id }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ops.GetAsync(Fleet + "/minivans?pageSize=101", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Fleet + "/dispatches", Ct)).StatusCode);
    }

    [Fact]
    public async Task Team_dispatch_is_private_idempotent_and_releases_resources_on_end()
    {
        using var ops = fixture.Client("ops"); using var stranger = fixture.Client("first");
        var team = await FleetTeam(ops);
        using var driver = fixture.Client(); driver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", team.DriverToken);
        using var crew = fixture.Client(); crew.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", team.CrewToken);
        Assert.Equal(HttpStatusCode.NoContent, (await driver.GetAsync("/api/v1/driver/dispatch", Ct)).StatusCode);
        var request = new CreateVanDispatchRequest(Guid.NewGuid(), team.Van.Id, team.Driver.Id, team.Crew.Id);
        var started = await FleetRead<VanDispatchResponse>(await ops.PostAsJsonAsync(Fleet + "/dispatches", request, Ct), HttpStatusCode.Created);
        var replay = await FleetRead<VanDispatchResponse>(await ops.PostAsJsonAsync(Fleet + "/dispatches", request, Ct), HttpStatusCode.Created);
        Assert.Equal(started.Id, replay.Id); Assert.Equal(started.StartedAt, replay.StartedAt);
        Assert.Equal(started.Id, (await driver.GetFromJsonAsync<VanDispatchResponse>("/api/v1/driver/dispatch", Ct))!.Id);
        Assert.Equal(started.Id, (await crew.GetFromJsonAsync<VanDispatchResponse>("/api/v1/crew/dispatch", Ct))!.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/api/v1/crew/dispatch", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Fleet + "/dispatches", request with { WarehouseId = Guid.NewGuid() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PutAsJsonAsync(Fleet + "/crew/" + team.Crew.Id, new UpdateCrewRequest("Prep", false, team.Crew.Version), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PutAsJsonAsync(Fleet + "/drivers/" + team.Driver.Id, new UpdateDriverRequest("Driver", false, team.Driver.Version), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Fleet + $"/dispatches/{started.Id}/end", new EndVanDispatchRequest(new byte[8]), Ct)).StatusCode);
        var ended = await FleetRead<VanDispatchResponse>(await ops.PostAsJsonAsync(Fleet + $"/dispatches/{started.Id}/end", new EndVanDispatchRequest(started.Version), Ct), HttpStatusCode.OK);
        Assert.NotNull(ended.EndedAt);
        Assert.Equal(HttpStatusCode.NoContent, (await driver.GetAsync("/api/v1/driver/dispatch", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ops.PostAsJsonAsync(Fleet + "/dispatches", request with { OperationId = Guid.NewGuid() }, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("van")]
    [InlineData("driver")]
    [InlineData("crew")]
    [InlineData("cross-role")]
    public async Task Concurrent_dispatches_cannot_share_resources_or_the_same_person_across_roles(string shared)
    {
        using var ops = fixture.Client("ops");
        var one = await FleetTeam(ops); var two = await FleetTeam(ops);
        var first = new CreateVanDispatchRequest(Guid.NewGuid(), one.Van.Id, one.Driver.Id, one.Crew.Id);
        var second = new CreateVanDispatchRequest(Guid.NewGuid(), shared == "van" ? one.Van.Id : two.Van.Id,
            shared == "driver" ? one.Driver.Id : two.Driver.Id, shared == "crew" ? one.Crew.Id : two.Crew.Id);
        if (shared == "cross-role")
        {
            var otherRole = await FleetRead<CrewResponse>(await ops.PostAsJsonAsync(Fleet + "/crew", new CreateCrewRequest(one.Driver.UserId, "Also crew"), Ct), HttpStatusCode.Created);
            second = second with { CrewMemberId = otherRole.Id };
        }
        var replies = await Task.WhenAll(ops.PostAsJsonAsync(Fleet + "/dispatches", first, Ct), ops.PostAsJsonAsync(Fleet + "/dispatches", second, Ct));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dispatch_requires_active_mobile_warehouse_distinct_people_and_no_open_delivery_on_end()
    {
        using var ops = fixture.Client("ops");
        var team = await FleetTeam(ops);
        var fixedVan = await FleetRead<WarehouseResponse>(await ops.PostAsJsonAsync("/api/v1/admin/inventory/warehouses", new WarehouseRequest("F" + Guid.NewGuid().ToString("N")[..12], "Fixed", "Fixed"), Ct), HttpStatusCode.Created);
        var request = new CreateVanDispatchRequest(Guid.NewGuid(), fixedVan.Id, team.Driver.Id, team.Crew.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Fleet + "/dispatches", request, Ct)).StatusCode);
        var same = await FleetRead<CrewResponse>(await ops.PostAsJsonAsync(Fleet + "/crew", new CreateCrewRequest(team.Driver.UserId, "Same person"), Ct), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.BadRequest, (await ops.PostAsJsonAsync(Fleet + "/dispatches", request with { WarehouseId = team.Van.Id, CrewMemberId = same.Id }, Ct)).StatusCode);
        var started = await FleetRead<VanDispatchResponse>(await ops.PostAsJsonAsync(Fleet + "/dispatches", request with { WarehouseId = team.Van.Id }, Ct), HttpStatusCode.Created);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            db.Shipments.Add(new Shipment { OrderId = Guid.NewGuid(), DriverId = team.Driver.Id, CustomerId = Guid.NewGuid(), Status = "Assigned", DropoffJson = "{}", AssignedAt = fixture.Clock.GetUtcNow() });
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await ops.PostAsJsonAsync(Fleet + $"/dispatches/{started.Id}/end", new EndVanDispatchRequest(started.Version), Ct)).StatusCode);
    }

    private async Task<(Guid Id, string Token)> FleetAccount(bool verified = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var phone = "+201" + Random.Shared.NextInt64(100000000, 999999999);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), PhoneNumber = phone, PhoneNumberConfirmed = verified };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);
        Assert.True(result.Succeeded);
        var session = new AuthSession { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, RefreshTokenHash = "", CreatedAt = fixture.Clock.GetUtcNow(), ExpiresAt = fixture.Clock.GetUtcNow().AddDays(1) };
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>(); db.AuthSessions.Add(session); await db.SaveChangesAsync(Ct);
        return (user.Id, scope.ServiceProvider.GetRequiredService<SessionTokens>().Issue(user, session).AccessToken);
    }

    private async Task<(WarehouseResponse Van, DriverResponse Driver, CrewResponse Crew, string DriverToken, string CrewToken)> FleetTeam(HttpClient ops)
    {
        var driverUser = await FleetAccount(); var crewUser = await FleetAccount();
        var driver = await FleetRead<DriverResponse>(await ops.PostAsJsonAsync(Fleet + "/drivers", new CreateDriverRequest(driverUser.Id, "Shift driver"), Ct), HttpStatusCode.Created);
        var crew = await FleetRead<CrewResponse>(await ops.PostAsJsonAsync(Fleet + "/crew", new CreateCrewRequest(crewUser.Id, "Shift crew"), Ct), HttpStatusCode.Created);
        var van = await FleetRead<WarehouseResponse>(await ops.PostAsJsonAsync("/api/v1/admin/inventory/warehouses", new WarehouseRequest("V" + Guid.NewGuid().ToString("N")[..12], "Test van", "Mobile"), Ct), HttpStatusCode.Created);
        return (van, driver, crew, driverUser.Token, crewUser.Token);
    }

    private static async Task<T> FleetRead<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
