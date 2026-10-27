using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Xunit;
namespace Velo.Auth.IntegrationTests;
public sealed class StaffAccessTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private static int counter = 20_000_000;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Provisioned_staff_exchange_refresh_and_revocation_use_server_permissions_only()
    {
        using var client = fixture.Client(); var phone = "+2012" + Interlocked.Increment(ref counter);
        await Login(client, phone); var user = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Ct))!;
        using(var scope = fixture.Factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<StaffAccessService>().SetAsync(user.Id, ["orders.manage"], Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", Ct)).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); await Login(client, phone);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/orders", Ct)).StatusCode);
        var exchange = await client.PostAsync("/api/v1/auth/staff/exchange", null, Ct); exchange.EnsureSuccessStatusCode();
        var staff = (await exchange.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
        Set(client, staff); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/orders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/payments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/staff/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/auth/staff/exchange", null, Ct)).StatusCode);
        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(staff.RefreshToken), Ct); refreshed.EnsureSuccessStatusCode();
        var next = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>(Ct))!;
        Set(client, next); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/orders", Ct)).StatusCode);
        using(var scope = fixture.Factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<StaffAccessService>().SetAsync(user.Id, [], Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/orders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(next.RefreshToken), Ct)).StatusCode);
    }
    [Fact]
    public async Task Customer_cannot_choose_staff_permissions_and_exchange_requires_recent_otp_session()
    {
        using var client = fixture.Client(); var phone = "+2012" + Interlocked.Increment(ref counter);
        await Login(client, phone); var user = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", Ct))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/staff/exchange", new { permissions = new[] { "orders.manage" } }, Ct)).StatusCode);
        using(var scope = fixture.Factory.Services.CreateScope())
        {
            var staff = scope.ServiceProvider.GetRequiredService<StaffAccessService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => staff.SetAsync(user.Id, ["unsupported.permission"], Ct));
            await staff.SetAsync(user.Id, ["orders.manage"], Ct);
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); await Login(client, phone);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/auth/staff/exchange", null, Ct)).StatusCode);
    }
    private static void Set(HttpClient client, TokenResponse response) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
    private static async Task Login(HttpClient client, string phone)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = phone }, Ct); response.EnsureSuccessStatusCode();
        var challenge = (await response.Content.ReadFromJsonAsync<OtpResponse>(Ct))!;
        var verified = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challenge.ChallengeId, code = challenge.DevelopmentCode }, Ct);
        verified.EnsureSuccessStatusCode(); Set(client, (await verified.Content.ReadFromJsonAsync<TokenResponse>(Ct))!);
    }
}
