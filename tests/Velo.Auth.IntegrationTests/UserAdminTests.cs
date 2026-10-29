using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Endpoints;
using Velo.Modules.Identity.Infrastructure;
using Xunit;

namespace Velo.Auth.IntegrationTests;

public sealed partial class UserAdminTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private static int counter = 30_000_000;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed record UserPage(AdminUserResponse[] Items, int TotalCount);

    [Fact]
    public async Task User_management_requires_separate_staff_permission_and_revokes_disabled_users()
    {
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/users", Ct)).StatusCode);
        using var customer = fixture.Client();
        var customerPhone = Phone();
        var target = await Login(customer, customerPhone);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/users", Ct)).StatusCode);
        using var staff = fixture.Client(); var staffPhone = Phone();
        var manager = await Login(staff, staffPhone);
        await Grant(manager.Id, ["catalog.manage"]);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); await Login(staff, staffPhone); await Exchange(staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/admin/users", Ct)).StatusCode);
        await Grant(manager.Id, ["users.manage"]);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); await Login(staff, staffPhone); await Exchange(staff);
        var response = await staff.GetAsync($"/api/v1/admin/users?search={Uri.EscapeDataString(customerPhone)}", Ct);
        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var page = (await response.Content.ReadFromJsonAsync<UserPage>(Ct))!;
        var user = Assert.Single(page.Items); Assert.Equal(target.Id, user.Id);
        var update = await staff.PutAsJsonAsync($"/api/v1/admin/users/{user.Id}", new AdminUserRequest("Test Customer", "test@example.com", false, user.Version), Ct);
        update.EnsureSuccessStatusCode();
        var updated = (await update.Content.ReadFromJsonAsync<AdminUserResponse>(Ct))!;
        Assert.False(updated.IsActive); Assert.NotEqual(user.Version, updated.Version);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/admin/users/{user.Id}", new AdminUserRequest("Stale", "test@example.com", true, user.Version), Ct)).StatusCode);
        var self = Assert.Single((await staff.GetFromJsonAsync<UserPage>($"/api/v1/admin/users?search={Uri.EscapeDataString(staffPhone)}", Ct))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync($"/api/v1/admin/users/{self.Id}", new AdminUserRequest("Self", "staff@example.com", false, self.Version), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/admin/users?pageSize=101", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync($"/api/v1/admin/users/{updated.Id}", new AdminUserRequest("Valid", "invalid email", false, updated.Version), Ct)).StatusCode);
        var enabled = await staff.PutAsJsonAsync($"/api/v1/admin/users/{updated.Id}", new AdminUserRequest("Test Customer", "test@example.com", true, updated.Version), Ct);
        enabled.EnsureSuccessStatusCode();
        // Re-enabling does not resurrect the old session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
    }
    private static string Phone() => "+2012" + Interlocked.Increment(ref counter);
    private async Task Grant(Guid id, string[] permissions)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StaffAccessService>().SetAsync(id, permissions, Ct);
    }
    private static async Task Exchange(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/auth/staff/exchange", null, Ct); response.EnsureSuccessStatusCode();
        Set(client, (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!);
    }
    private static void Set(HttpClient client, TokenResponse tokens) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    private static async Task<CurrentUserResponse> Login(HttpClient client, string phone)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new RequestOtpRequest(phone), Ct); response.EnsureSuccessStatusCode();
        var challenge = (await response.Content.ReadFromJsonAsync<OtpResponse>(Ct))!;
        var verify = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new VerifyOtpRequest(challenge.ChallengeId, challenge.DevelopmentCode!), Ct); verify.EnsureSuccessStatusCode();
        Set(client, (await verify.Content.ReadFromJsonAsync<TokenResponse>(Ct))!);
        return (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Ct))!;
    }
}
