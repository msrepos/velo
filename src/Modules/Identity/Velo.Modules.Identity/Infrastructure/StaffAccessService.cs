using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;
namespace Velo.Modules.Identity.Infrastructure;
public sealed class StaffAccessService(IdentityDbContext db, UserManager<ApplicationUser> users,
    AuthDatabaseLock locks, SessionTokens tokens, TimeProvider time)
{
    public const string ClaimType = "velo.staff.permission";
    public static readonly string[] Allowed = ["catalog.manage", "inventory.manage", "orders.manage", "delivery.manage", "payments.manage"];
    public async Task<string[]> PermissionsAsync(Guid user, CancellationToken ct) => await db.UserClaims.AsNoTracking()
        .Where(x => x.UserId == user && x.ClaimType == ClaimType && x.ClaimValue != null && Allowed.Contains(x.ClaimValue))
        .Select(x => x.ClaimValue!).Distinct().OrderBy(x => x).ToArrayAsync(ct);
    public async Task SetAsync(Guid userId, string[] permissions, CancellationToken ct)
    {
        if (permissions.Any(x => !Allowed.Contains(x)) || permissions.Distinct().Count() != permissions.Length)
            throw new InvalidOperationException("Choose unique supported staff permissions.");
        await locks.ExecuteAsync(["user:" + userId.ToString("N")], async () =>
        {
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null || !user.IsActive || !user.PhoneNumberConfirmed) throw new InvalidOperationException("Staff provisioning requires an active verified user.");
            db.UserClaims.RemoveRange(await db.UserClaims.Where(x => x.UserId == userId && x.ClaimType == ClaimType).ToArrayAsync(ct));
            db.UserClaims.AddRange(permissions.Select(x => new IdentityUserClaim<Guid> { UserId = userId, ClaimType = ClaimType, ClaimValue = x }));
            var result = await users.UpdateSecurityStampAsync(user);
            if (!result.Succeeded) throw new InvalidOperationException("Could not update staff access.");
            return true;
        }, ct);
    }
    public Task<TokenResponse> ExchangeAsync(Guid userId, Guid sourceId, CancellationToken ct) =>
        locks.ExecuteAsync(["user:" + userId.ToString("N"), "session:" + sourceId.ToString("N")], async () =>
        {
            var now = time.GetUtcNow();
            var source = await db.AuthSessions.SingleOrDefaultAsync(x => x.Id == sourceId && x.UserId == userId, ct);
            var user = await users.FindByIdAsync(userId.ToString());
            if (source is null || source.IsStaff || source.RevokedAt != null || source.ExpiresAt <= now || source.CreatedAt < now.AddMinutes(-5) ||
                user is null || !user.IsActive || !user.PhoneNumberConfirmed || user.LockoutEnd > now || user.SecurityStamp != source.SecurityStamp)
                throw new AuthException(403, "staff_reauthentication_required");
            var permissions = await PermissionsAsync(userId, ct);
            if (permissions.Length == 0) throw new AuthException(403, "staff_access_not_granted");
            var session = new AuthSession { Id = Guid.NewGuid(), UserId = userId, IsStaff = true, SecurityStamp = user.SecurityStamp!,
                RefreshTokenHash = "", CreatedAt = now, ExpiresAt = now.AddHours(8) };
            db.AuthSessions.Add(session); return tokens.Issue(user, session, permissions);
        }, ct);
}
