using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;

namespace Velo.Modules.Identity.Infrastructure;

public sealed class AuthService(
    IdentityDbContext db, UserManager<ApplicationUser> users, AuthDatabaseLock locks,
    AuthCryptography crypto, SessionTokens tokens, IOtpSender sender,
    IOptions<AuthOptions> options, TimeProvider time, IHostEnvironment environment, StaffAccessService staffAccess)
{
    private readonly AuthOptions settings = options.Value;

    public async Task<OtpResponse> RequestOtpAsync(string phoneInput, string origin, CancellationToken ct,
        RegisterRequest? registration = null)
    {
        var phone = PhoneNumber.Normalize(phoneInput);
        string? name = null;
        string? email = null;
        if (registration is not null)
        {
            name = registration.Name?.Trim();
            email = registration.Email?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || name.Any(char.IsControl))
                throw new AuthException(400, "invalid_name");
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256 ||
                email.Any(x => char.IsWhiteSpace(x) || char.IsControl(x)) ||
                !new EmailAddressAttribute().IsValid(email))
                throw new AuthException(400, "invalid_email");
        }
        var clientKey = crypto.HashPrivate("client:" + origin);
        var phoneKey = PhoneLock(phone);
        var code = AuthCryptography.GenerateCode();
        var challenge = await RunAsync<OtpChallenge>([phoneKey, "client:" + clientKey], async () =>
        {
            var now = time.GetUtcNow();
            if (!await TakeLimitAsync("request:" + clientKey, settings.OtpRequestsPerClientPerHour, now, ct))
                return Fail<OtpChallenge>(429, "otp_rate_limited", 3600);

            var recent = db.OtpChallenges.Where(x => x.PhoneNumber == phone && x.CreatedAt > now.AddHours(-1));
            var latest = await recent.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (latest is not null && latest.CreatedAt.AddSeconds(settings.OtpResendSeconds) > now)
                return Fail<OtpChallenge>(429, "otp_rate_limited",
                    (int)Math.Ceiling((latest.CreatedAt.AddSeconds(settings.OtpResendSeconds) - now).TotalSeconds));
            if (await recent.CountAsync(ct) >= settings.OtpRequestsPerPhonePerHour)
                return Fail<OtpChallenge>(429, "otp_rate_limited", 3600);

            await db.OtpChallenges.Where(x => x.PhoneNumber == phone &&
                x.ConsumedAt == null && x.InvalidatedAt == null).ExecuteUpdateAsync(
                    update => update.SetProperty(x => x.InvalidatedAt, now), ct);
            var item = new OtpChallenge
            {
                Id = Guid.NewGuid(), PhoneNumber = phone, ClientKey = clientKey,
                RegistrationName = name, RegistrationEmail = email,
                CodeHash = "", CreatedAt = now, ExpiresAt = now.AddMinutes(settings.OtpLifetimeMinutes)
            };
            item.CodeHash = crypto.HashCode(item.Id, phone, code);
            db.OtpChallenges.Add(item);
            return Ok(item);
        }, ct);

        // No provider call is made while holding SQL locks. Pending/failed deliveries
        // cannot be verified. On process failure they expire and a later resend replaces them.
        try { await sender.SendAsync(phone, code, ct); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await locks.ExecuteAsync([phoneKey], async () =>
            {
                var item = await db.OtpChallenges.SingleAsync(x => x.Id == challenge.Id, ct);
                item.InvalidatedAt = time.GetUtcNow();
                return true;
            }, ct);
            throw new AuthException(503, "otp_delivery_unavailable");
        }

        await locks.ExecuteAsync([phoneKey], async () =>
        {
            var item = await db.OtpChallenges.SingleAsync(x => x.Id == challenge.Id, ct);
            item.SentAt = time.GetUtcNow();
            return true;
        }, ct);

        return new OtpResponse(challenge.Id, settings.OtpLifetimeMinutes * 60,
            settings.OtpResendSeconds,
            environment.IsDevelopment() && settings.ExposeDevelopmentCodes ? code : null);
    }

    public async Task<TokenResponse> VerifyOtpAsync(Guid challengeId, string code, string origin, CancellationToken ct)
    {
        if (challengeId == Guid.Empty || code is null || code.Length != 6 || code.Any(x => x is < '0' or > '9'))
            throw new AuthException(400, "invalid_request");

        var phone = await db.OtpChallenges.Where(x => x.Id == challengeId)
            .Select(x => x.PhoneNumber).SingleOrDefaultAsync(ct);
        var clientKey = crypto.HashPrivate("client:" + origin);

        return await RunAsync<TokenResponse>(
            [phone is null ? "missing:" + challengeId : PhoneLock(phone), "client:" + clientKey], async () =>
        {
            var now = time.GetUtcNow();
            if (!await TakeLimitAsync("verify:" + clientKey, settings.OtpVerificationsPerClientPerHour, now, ct))
                return Fail<TokenResponse>(429, "otp_rate_limited", 3600);

            var challenge = await db.OtpChallenges.SingleOrDefaultAsync(x => x.Id == challengeId, ct);
            if (challenge is null || challenge.SentAt is null || challenge.ExpiresAt <= now ||
                challenge.InvalidatedAt is not null || challenge.ConsumedAt is not null ||
                challenge.Attempts >= settings.OtpMaxAttempts)
                return Fail<TokenResponse>(400, "invalid_or_expired_code");

            challenge.Attempts++;
            if (!AuthCryptography.Matches(challenge.CodeHash, crypto.HashCode(challenge.Id, challenge.PhoneNumber, code)))
                return Fail<TokenResponse>(400, "invalid_or_expired_code");

            var user = await db.Users.SingleOrDefaultAsync(x => x.PhoneNumber == challenge.PhoneNumber, ct);
            if (user is not null && (!user.IsActive || user.LockoutEnd > now))
                return Fail<TokenResponse>(400, "invalid_or_expired_code");

            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(), PhoneNumber = challenge.PhoneNumber,
                    Name = challenge.RegistrationName, Email = challenge.RegistrationEmail,
                    UserName = challenge.PhoneNumber, PhoneNumberConfirmed = true
                };
                var result = await users.CreateAsync(user);
                if (!result.Succeeded) throw new InvalidOperationException("Could not create the verified customer.");
            }
            else if (!user.PhoneNumberConfirmed)
            {
                user.PhoneNumberConfirmed = true;
                var result = await users.UpdateAsync(user);
                if (!result.Succeeded) throw new InvalidOperationException("Could not confirm the verified phone.");
            }

            // Customer OTP never elevates to staff/admin roles, even for an account
            // which has those roles through a separate staff provisioning flow.
            if (!await users.IsInRoleAsync(user, "Customer"))
            {
                var result = await users.AddToRoleAsync(user, "Customer");
                if (!result.Succeeded) throw new InvalidOperationException("Could not assign the customer role.");
            }

            challenge.ConsumedAt = now;
            var session = new AuthSession
            {
                Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!,
                RefreshTokenHash = "", CreatedAt = now, ExpiresAt = now.AddDays(settings.SessionDays)
            };
            db.AuthSessions.Add(session);
            return Ok(tokens.Issue(user, session));
        }, ct);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new AuthException(401, "invalid_session");
        return new CurrentUserResponse(user.Id, user.PhoneNumber!, ["Customer"], user.Name, user.Email);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var ticket = tokens.ReadRefresh(refreshToken);
        if (ticket is null ||
            !Guid.TryParse(ticket.Principal.FindFirstValue(SessionTokens.SessionClaim), out var sessionId) ||
            !Guid.TryParse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(ticket.Principal.FindFirstValue(SessionTokens.VersionClaim), out var version))
            throw new AuthException(401, "invalid_session");

        return await RunAsync<TokenResponse>([UserLock(userId), SessionLock(sessionId)], async () =>
        {
            var now = time.GetUtcNow();
            var session = await db.AuthSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
            if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
                return Fail<TokenResponse>(401, "invalid_session");

            if (version != session.RefreshVersion ||
                !AuthCryptography.Matches(session.RefreshTokenHash, AuthCryptography.HashToken(refreshToken)))
            {
                session.RevokedAt = now; // Commit this revocation before returning 401.
                return Fail<TokenResponse>(401, "invalid_session");
            }

            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user is null || !user.IsActive || !user.PhoneNumberConfirmed ||
                user.LockoutEnd > now || user.SecurityStamp != session.SecurityStamp)
            {
                session.RevokedAt = now;
                return Fail<TokenResponse>(401, "invalid_session");
            }

            session.RefreshVersion++;
            var permissions = session.IsStaff ? await staffAccess.PermissionsAsync(user.Id, ct) : null;
            if (session.IsStaff && permissions!.Length == 0)
            { session.RevokedAt = now; return Fail<TokenResponse>(401, "invalid_session"); }
            return Ok(tokens.Issue(user, session, permissions));
        }, ct);
    }

    public async Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await locks.ExecuteAsync([UserLock(userId), SessionLock(sessionId)], async () =>
        {
            await db.AuthSessions.Where(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, time.GetUtcNow()), ct);
            return true;
        }, ct);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        await locks.ExecuteAsync([UserLock(userId)], async () =>
        {
            await db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, time.GetUtcNow()), ct);
            // An OTP verification which raced with logout-all cannot create a usable
            // session with the old stamp after this operation commits.
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                var result = await users.UpdateSecurityStampAsync(user);
                if (!result.Succeeded) throw new InvalidOperationException("Could not revoke the user's sessions.");
            }
            return true;
        }, ct);
    }

    public async Task<SessionResponse[]> GetSessionsAsync(Guid userId, Guid currentSession, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.AuthSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SessionResponse(x.Id, x.CreatedAt, x.ExpiresAt, x.Id == currentSession))
            .ToArrayAsync(ct);
    }

    private async Task<bool> TakeLimitAsync(string key, int maximum, DateTimeOffset now, CancellationToken ct)
    {
        var window = await db.AuthThrottles.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (window is null)
        {
            window = new AuthThrottle { Key = key, WindowStartedAt = now };
            db.AuthThrottles.Add(window);
        }
        if (window.WindowStartedAt.AddHours(1) <= now)
        {
            window.WindowStartedAt = now;
            window.Count = 0;
        }
        if (window.Count >= maximum) return false;
        window.Count++;
        return true;
    }

    private string PhoneLock(string phone) => "phone:" + crypto.HashPrivate(phone);
    private static string UserLock(Guid userId) => "user:" + userId.ToString("N");
    private static string SessionLock(Guid sessionId) => "session:" + sessionId.ToString("N");
    private sealed record Outcome<T>(T? Value, AuthException? Error);
    private static Outcome<T> Ok<T>(T value) => new(value, null);
    private static Outcome<T> Fail<T>(int status, string code, int? retryAfter = null) =>
        new(default, new AuthException(status, code, retryAfter));

    private async Task<T> RunAsync<T>(string[] keys, Func<Task<Outcome<T>>> action, CancellationToken ct)
    {
        var outcome = await locks.ExecuteAsync(keys, action, ct);
        // Throw after commit so rejected attempts, rate counters and replay revocation persist.
        if (outcome.Error is not null) throw outcome.Error;
        return outcome.Value!;
    }
}
