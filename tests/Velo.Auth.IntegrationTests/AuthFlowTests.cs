using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Domain;
using Xunit;

namespace Velo.Auth.IntegrationTests;

public sealed class AuthFlowTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private static int phoneSequence;
    private static string NewPhone() => "+2010" + Interlocked.Increment(ref phoneSequence).ToString("D8");

    [Fact]
    public async Task Customer_can_login_refresh_and_logout_with_immediate_revocation()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        var phone = NewPhone();
        var challenge = await Request(client, phone);
        var duplicate = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = phone }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, duplicate.StatusCode);
        Assert.NotNull(duplicate.Headers.RetryAfter);

        var token = await Verify(client, challenge);
        Assert.NotEmpty(token.RefreshToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresIn <= 900);
        Authorize(client, token);
        var user = await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(phone, user!.PhoneNumber);
        Assert.Equal(["Customer"], user.Roles);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { token.RefreshToken }, TestContext.Current.CancellationToken);
        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = (await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        Assert.NotEqual(token.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(token.SessionId, refreshed.SessionId);
        Authorize(client, refreshed);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshed.RefreshToken }, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Refresh_replay_revokes_the_whole_session_including_new_access_token()
    {
        using var client = fixture.Client();
        var original = await Login(client, NewPhone());
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { original.RefreshToken }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var renewed = (await response.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { original.RefreshToken }, TestContext.Current.CancellationToken)).StatusCode);
        Authorize(client, renewed);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { renewed.RefreshToken }, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Otp_is_single_use_and_creates_one_user_under_concurrent_verification()
    {
        using var first = fixture.Client();
        using var second = fixture.Client();
        var phone = NewPhone();
        var challenge = await Request(first, phone);
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challenge.ChallengeId, code = challenge.DevelopmentCode }, TestContext.Current.CancellationToken),
            second.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challenge.ChallengeId, code = challenge.DevelopmentCode }, TestContext.Current.CancellationToken));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(x => x.PhoneNumber == phone, TestContext.Current.CancellationToken));
        var stored = await db.OtpChallenges.SingleAsync(x => x.Id == challenge.ChallengeId, TestContext.Current.CancellationToken);
        Assert.NotNull(stored.ConsumedAt);
        Assert.NotEqual(challenge.DevelopmentCode, stored.CodeHash);
    }

    [Fact]
    public async Task Wrong_codes_exhaust_a_persisted_attempt_budget()
    {
        using var client = fixture.Client();
        var challenge = await Request(client, NewPhone());
        var wrong = challenge.DevelopmentCode == "000000" ? "111111" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
                new { challenge.ChallengeId, code = wrong }, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { challenge.ChallengeId, code = challenge.DevelopmentCode }, TestContext.Current.CancellationToken)).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(5, (await db.OtpChallenges.SingleAsync(x => x.Id == challenge.ChallengeId, TestContext.Current.CancellationToken)).Attempts);
    }

    [Fact]
    public async Task Expired_challenge_is_rejected_without_creating_a_user()
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        var challenge = await Request(client, phone);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { challenge.ChallengeId, code = challenge.DevelopmentCode }, TestContext.Current.CancellationToken)).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.AnyAsync(x => x.PhoneNumber == phone, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resend_invalidates_previous_code_and_phone_quota_survives_new_challenges()
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        var original = await Request(client, phone);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var newer = await Request(client, phone);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { original.ChallengeId, code = original.DevelopmentCode }, TestContext.Current.CancellationToken)).StatusCode);
        await Verify(client, newer);
        for (var i = 0; i < 3; i++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(61));
            await Request(client, phone);
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/otp/request",
            new { phoneNumber = phone }, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Expired_access_can_refresh_but_absolute_session_expiry_cannot()
    {
        using var client = fixture.Client();
        var token = await Login(client, NewPhone());
        Authorize(client, token);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { token.RefreshToken }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var renewed = (await response.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        Authorize(client, renewed);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { renewed.RefreshToken }, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_all_revokes_every_device()
    {
        using var first = fixture.Client();
        using var second = fixture.Client();
        var phone = NewPhone();
        var firstToken = await Login(first, phone);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var secondToken = await Login(second, phone);
        Authorize(first, firstToken);
        Authorize(second, secondToken);
        var sessions = await first.GetFromJsonAsync<SessionResponse[]>("/api/v1/auth/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(2, sessions!.Length);
        Assert.Single(sessions, x => x.IsCurrent);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/v1/auth/logout-all", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.PostAsJsonAsync("/api/v1/auth/refresh",
            new { secondToken.RefreshToken }, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_revoke_another_customers_session()
    {
        using var first = fixture.Client();
        using var second = fixture.Client();
        var firstToken = await Login(first, NewPhone());
        var secondToken = await Login(second, NewPhone());
        Authorize(first, firstToken);
        Authorize(second, secondToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await first.DeleteAsync("/api/v1/auth/sessions/" + secondToken.SessionId, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await first.DeleteAsync("/api/v1/auth/sessions/" + firstToken.SessionId, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Disabled_user_loses_access_and_cannot_login_or_refresh()
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        var token = await Login(client, phone);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Users.Where(x => x.PhoneNumber == phone)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false), TestContext.Current.CancellationToken);
        }
        Authorize(client, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { token.RefreshToken }, TestContext.Current.CancellationToken)).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var challenge = await Request(client, phone);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { challenge.ChallengeId, code = challenge.DevelopmentCode }, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Customer_otp_does_not_issue_administrative_roles()
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        await Login(client, phone);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var userId = await db.Users.Where(x => x.PhoneNumber == phone).Select(x => x.Id).SingleAsync(TestContext.Current.CancellationToken);
            var roleId = Guid.NewGuid();
            db.Roles.Add(new IdentityRole<Guid> { Id = roleId, Name = "Admin", NormalizedName = "ADMIN" });
            db.UserRoles.Add(new IdentityUserRole<Guid> { RoleId = roleId, UserId = userId });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var token = await Login(client, phone);
        Authorize(client, token);
        var user = await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(["Customer"], user!.Roles);
    }

    [Fact]
    public async Task Tampered_tokens_and_access_tokens_used_as_refresh_are_rejected()
    {
        using var client = fixture.Client();
        var token = await Login(client, NewPhone());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = token.AccessToken }, TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken + "tamper");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Failed_sms_delivery_cannot_be_verified_and_can_be_retried_after_cooldown()
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        fixture.Sms.FailNext();
        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = phone },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var failed = await db.OtpChallenges.SingleAsync(x => x.PhoneNumber == phone,
                TestContext.Current.CancellationToken);
            Assert.Null(failed.SentAt);
            Assert.NotNull(failed.InvalidatedAt);
            Assert.False(await db.Users.AnyAsync(x => x.PhoneNumber == phone,
                TestContext.Current.CancellationToken));
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await Login(client, phone);
    }

    [Fact]
    public async Task Concurrent_refreshes_rotate_once_and_replay_revokes_the_resulting_session()
    {
        using var first = fixture.Client();
        using var second = fixture.Client();
        var original = await Login(first, NewPhone());
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/v1/auth/refresh", new { original.RefreshToken }, TestContext.Current.CancellationToken),
            second.PostAsJsonAsync("/api/v1/auth/refresh", new { original.RefreshToken }, TestContext.Current.CancellationToken));
        var accepted = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var renewed = (await accepted.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
        Authorize(first, renewed);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await first.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("verify")]
    public async Task Per_client_limits_are_persisted_in_sql_server(string operation)
    {
        using var client = fixture.Client();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<AuthCryptography>();
        var key = operation + ":" + crypto.HashPrivate("client:unknown");
        var throttle = await db.AuthThrottles.SingleOrDefaultAsync(x => x.Key == key, TestContext.Current.CancellationToken);
        if (throttle is null)
        {
            throttle = new AuthThrottle { Key = key, WindowStartedAt = fixture.Clock.GetUtcNow() };
            db.AuthThrottles.Add(throttle);
        }
        throttle.WindowStartedAt = fixture.Clock.GetUtcNow();
        throttle.Count = 1000;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        try
        {
            var response = operation == "request"
                ? await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = NewPhone() }, TestContext.Current.CancellationToken)
                : await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = Guid.NewGuid(), code = "000000" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        finally
        {
            await db.AuthThrottles.Where(x => x.Key == key)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Count, 0), TestContext.Current.CancellationToken);
            Assert.Equal(0, await db.AuthThrottles.AsNoTracking().Where(x => x.Key == key)
                .Select(x => x.Count).SingleAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Registration_saves_profile_only_after_otp_and_login_preserves_it()
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { phoneNumber = phone, email = "  customer@example.com  ", name = "  محمد أحمد  " },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var challenge = (await response.Content.ReadFromJsonAsync<OtpResponse>(TestContext.Current.CancellationToken))!;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.False(await db.Users.AnyAsync(x => x.PhoneNumber == phone, TestContext.Current.CancellationToken));
        }
        Authorize(client, await Verify(client, challenge));
        var created = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", TestContext.Current.CancellationToken))!;
        Assert.Equal("محمد أحمد", created.Name);
        Assert.Equal("customer@example.com", created.Email);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == created.Id, TestContext.Current.CancellationToken);
            Assert.Equal("CUSTOMER@EXAMPLE.COM", user.NormalizedEmail);
            Assert.False(user.EmailConfirmed);
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        Authorize(client, await Login(client, phone));
        var loggedIn = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", TestContext.Current.CancellationToken))!;
        Assert.Equal(created, loggedIn with { Roles = created.Roles });

        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var repeated = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { phoneNumber = phone, email = "different@example.com", name = "Different Name" },
            TestContext.Current.CancellationToken);
        repeated.EnsureSuccessStatusCode();
        var repeatedChallenge = (await repeated.Content.ReadFromJsonAsync<OtpResponse>(TestContext.Current.CancellationToken))!;
        Authorize(client, await Verify(client, repeatedChallenge));
        var unchanged = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me", TestContext.Current.CancellationToken))!;
        Assert.Equal(created.Id, unchanged.Id);
        Assert.Equal(created.Name, unchanged.Name);
        Assert.Equal(created.Email, unchanged.Email);
    }

    [Theory]
    [InlineData("not-an-email", "Customer", "invalid_email")]
    [InlineData("", "Customer", "invalid_email")]
    [InlineData("customer@example.com", "  ", "invalid_name")]
    [InlineData("customer@example.com", "Bad\nName", "invalid_name")]
    public async Task Invalid_registration_fields_do_not_create_challenges(string email, string name, string error)
    {
        using var client = fixture.Client();
        var phone = NewPhone();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { phoneNumber = phone, email, name }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.OtpChallenges.AnyAsync(x => x.PhoneNumber == phone, TestContext.Current.CancellationToken));
    }

    private async Task<OtpResponse> Request(HttpClient client, string phone)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = phone }, TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var counts = await db.AuthThrottles.AsNoTracking().Select(x => x.Count)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            var phoneRows = await db.OtpChallenges.CountAsync(x => x.PhoneNumber == phone,
                TestContext.Current.CancellationToken);
            Assert.Fail($"OTP failed: {(int)response.StatusCode}; phone rows={phoneRows}; throttle counts={string.Join(',', counts)}");
        }
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var challenge = (await response.Content.ReadFromJsonAsync<OtpResponse>(TestContext.Current.CancellationToken))!;
        Assert.Matches("^[0-9]{6}$", challenge.DevelopmentCode!);
        return challenge;
    }

    private static async Task<TokenResponse> Verify(HttpClient client, OtpResponse challenge)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { challenge.ChallengeId, code = challenge.DevelopmentCode }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<TokenResponse> Login(HttpClient client, string phone) =>
        await Verify(client, await Request(client, phone));

    private static void Authorize(HttpClient client, TokenResponse token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
}
