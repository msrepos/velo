using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Inventory.Application;
using Xunit;

namespace Velo.Auth.IntegrationTests;

public sealed partial class UserAdminTests
{
    private const string UserApi = "/api/v1/admin/users";

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    [InlineData("Driver")]
    [InlineData("Crew")]
    public async Task Admin_registers_each_role_without_bypassing_phone_verification(string role)
    {
        using var admin = await UserManagerClient();
        var phone = Phone();
        var response = await admin.PostAsJsonAsync(UserApi, new CreateAdminUserRequest("New " + role, "new@example.com", phone, role), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var created = (await response.Content.ReadFromJsonAsync<AdminUserResponse>(Ct))!;
        Assert.Equal(role, created.Role); Assert.False(created.PhoneNumberConfirmed);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var permissions = await db.UserClaims.Where(x => x.UserId == created.Id && x.ClaimType == StaffAccessService.ClaimType).Select(x => x.ClaimValue).ToArrayAsync(Ct);
            Assert.Equal(role == "Admin" ? StaffAccessService.Allowed.Order() : role == "Crew" ? new[] { "orders.manage" } : [], permissions.Order());
            var delivery = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            Assert.Equal(role == "Driver", await delivery.Drivers.AnyAsync(x => x.UserId == created.Id && x.IsActive, Ct));
            Assert.Equal(role == "Crew", await delivery.CrewMembers.AnyAsync(x => x.UserId == created.Id && x.IsActive, Ct));
        }
        using var member = fixture.Client(); await Login(member, phone);
        if (role is "Admin" or "Crew")
        {
            await Exchange(member);
            var permissions = await member.GetFromJsonAsync<StaffView>("/api/v1/auth/staff/me", Ct);
            Assert.Contains(role == "Admin" ? "users.manage" : "orders.manage", permissions!.Permissions);
        }
        if (role == "Driver") Assert.Equal(HttpStatusCode.NoContent, (await member.GetAsync("/api/v1/driver/dispatch", Ct)).StatusCode);
        if (role == "Crew") Assert.Equal(HttpStatusCode.NoContent, (await member.GetAsync("/api/v1/crew/dispatch", Ct)).StatusCode);
        var listed = Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(phone)}", Ct))!.Items);
        Assert.True(listed.PhoneNumberConfirmed); Assert.Equal(role, listed.Role);
    }

    [Fact]
    public async Task Registration_requires_user_management_valid_role_and_unique_normalized_phone()
    {
        using var admin = await UserManagerClient(); using var ordinary = fixture.Client();
        var phone = Phone(); var request = new CreateAdminUserRequest("Customer", "customer@example.com", phone);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ordinary.PostAsJsonAsync(UserApi, request, Ct)).StatusCode);
        await Login(ordinary, Phone());
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.PostAsJsonAsync(UserApi, request with { Role = "Admin" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(UserApi, request with { Role = "Owner" }, Ct)).StatusCode);
        var replies = await Task.WhenAll(admin.PostAsJsonAsync(UserApi, request, Ct), admin.PostAsJsonAsync(UserApi, request with { PhoneNumber = phone.Insert(3, " ") }, Ct));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(phone)}", Ct))!.Items);
    }

    [Fact]
    public async Task Role_changes_sync_membership_and_permissions_and_revoke_previous_sessions()
    {
        using var admin = await UserManagerClient(); var phone = Phone();
        var created = await Created(admin, phone, "Crew");
        using var member = fixture.Client(); await Login(member, phone); await Exchange(member);
        var promoted = await Updated(admin, created, "Admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); await Login(member, phone); await Exchange(member);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(UserApi, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync(UserApi + "/" + promoted.Id,
            new AdminUserRequest(promoted.Name, promoted.Email, true, promoted.Version, "Customer"), Ct)).StatusCode);
        var driver = await Updated(admin, promoted, "Driver");
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync(UserApi, Ct)).StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            Assert.False((await db.CrewMembers.SingleAsync(x => x.UserId == created.Id, Ct)).IsActive);
            Assert.True((await db.Drivers.SingleAsync(x => x.UserId == created.Id, Ct)).IsActive);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<StaffAccessService>().PermissionsAsync(created.Id, Ct));
        }
        await Updated(admin, driver, "Customer");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(UserApi + "/" + driver.Id,
            new AdminUserRequest(driver.Name, driver.Email, true, driver.Version, "Crew"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Active_shift_blocks_role_removal_and_user_deactivation_atomically()
    {
        using var admin = await UserManagerClient();
        var driverPhone = Phone(); var crewPhone = Phone();
        var driverUser = await Created(admin, driverPhone, "Driver"); var crewUser = await Created(admin, crewPhone, "Crew");
        using var driverClient = fixture.Client(); using var crewClient = fixture.Client();
        await Login(driverClient, driverPhone); await Login(crewClient, crewPhone);
        Guid driverId; Guid crewId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            driverId = (await db.Drivers.SingleAsync(x => x.UserId == driverUser.Id, Ct)).Id;
            crewId = (await db.CrewMembers.SingleAsync(x => x.UserId == crewUser.Id, Ct)).Id;
        }
        // OTP verification updates the account concurrency stamp.
        driverUser = Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(driverPhone)}", Ct))!.Items);
        crewUser = Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(crewPhone)}", Ct))!.Items);
        var vanReply = await admin.PostAsJsonAsync("/api/v1/admin/inventory/warehouses", new WarehouseRequest("V" + Guid.NewGuid().ToString("N")[..12], "Role test van", "Mobile"), Ct);
        vanReply.EnsureSuccessStatusCode(); var van = (await vanReply.Content.ReadFromJsonAsync<WarehouseResponse>(Ct))!;
        var shift = await admin.PostAsJsonAsync("/api/v1/admin/delivery/dispatches", new CreateVanDispatchRequest(Guid.NewGuid(), van.Id, driverId, crewId), Ct);
        shift.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(UserApi + "/" + driverUser.Id,
            new AdminUserRequest(driverUser.Name, driverUser.Email, true, driverUser.Version, "Customer"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(UserApi + "/" + crewUser.Id,
            new AdminUserRequest(crewUser.Name, crewUser.Email, false, crewUser.Version, "Crew"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await driverClient.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        Assert.Equal("Driver", Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(driverPhone)}", Ct))!.Items).Role);
    }

    private sealed record StaffView(string[] Permissions);
    [Theory]
    [InlineData("Assigned")]
    [InlineData("OutForDelivery")]
    [InlineData("Failed")]
    public async Task Unfinished_delivery_blocks_driver_role_removal(string status)
    {
        using var admin = await UserManagerClient();
        var user = await Created(admin, Phone(), "Driver");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            var driver = await db.Drivers.SingleAsync(x => x.UserId == user.Id, Ct);
            db.Shipments.Add(new Velo.Modules.Delivery.Domain.Shipment { OrderId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), DriverId = driver.Id,
                Status = status, DropoffJson = "{}", AssignedAt = fixture.Clock.GetUtcNow() });
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(UserApi + "/" + user.Id,
            new AdminUserRequest(user.Name, user.Email, true, user.Version, "Customer"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(UserApi + "/" + user.Id,
            new AdminUserRequest(user.Name, user.Email, false, user.Version, "Driver"), Ct)).StatusCode);
        Assert.Equal("Driver", Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(user.PhoneNumber!)}", Ct))!.Items).Role);
    }

    [Fact]
    public async Task Saving_existing_admin_preserves_custom_permissions()
    {
        using var admin = await UserManagerClient(); using var legacy = fixture.Client();
        var phone = Phone(); var target = await Login(legacy, phone);
        await Grant(target.Id, ["users.manage", "orders.manage"]);
        var user = Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(phone)}", Ct))!.Items);
        var saved = await admin.PutAsJsonAsync(UserApi + "/" + user.Id,
            new AdminUserRequest("Edited admin", "legacy@example.com", true, user.Version, "Admin"), Ct);
        saved.EnsureSuccessStatusCode();
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.Equal(new[] { "orders.manage", "users.manage" }, await scope.ServiceProvider.GetRequiredService<StaffAccessService>().PermissionsAsync(target.Id, Ct));
    }

    private async Task<HttpClient> UserManagerClient()
    {
        var client = fixture.Client(); var phone = Phone(); var user = await Login(client, phone);
        await Grant(user.Id, StaffAccessService.Allowed);
        // Seed the operator session instead of consuming the production HTTP OTP
        // rate budget for fixture setup in every role test. Member OTP remains real.
        using var scope = fixture.Factory.Services.CreateScope();
        var account = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Velo.Modules.Identity.Infrastructure.Persistence.Models.ApplicationUser>>().FindByIdAsync(user.Id.ToString()))!;
        var session = new Velo.Modules.Identity.Domain.AuthSession { Id = Guid.NewGuid(), UserId = account.Id, IsStaff = true,
            SecurityStamp = account.SecurityStamp!, RefreshTokenHash = "", CreatedAt = fixture.Clock.GetUtcNow(), ExpiresAt = fixture.Clock.GetUtcNow().AddHours(8) };
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<SessionTokens>().Issue(account, session, StaffAccessService.Allowed);
        db.AuthSessions.Add(session); await db.SaveChangesAsync(Ct);
        Set(client, tokens);
        return client;
    }
    private static async Task<AdminUserResponse> Created(HttpClient admin, string phone, string role)
    {
        var response = await admin.PostAsJsonAsync(UserApi, new CreateAdminUserRequest("Test " + role, "role@example.com", phone, role), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminUserResponse>(Ct))!;
    }
    private static async Task<AdminUserResponse> Updated(HttpClient admin, AdminUserResponse user, string role)
    {
        // Reload since OTP may have updated the version since registration.
        user = Assert.Single((await admin.GetFromJsonAsync<UserPage>($"{UserApi}?search={Uri.EscapeDataString(user.PhoneNumber!)}", Ct))!.Items);
        var response = await admin.PutAsJsonAsync(UserApi + "/" + user.Id,
            new AdminUserRequest(user.Name, user.Email, true, user.Version, role), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminUserResponse>(Ct))!;
    }
}
