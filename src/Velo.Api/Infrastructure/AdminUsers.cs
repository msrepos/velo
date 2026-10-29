using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Velo.Modules.Delivery.Domain;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;

namespace Velo.Api.Infrastructure;

// Account permissions and fleet profiles commit on one physical SQL transaction.
public sealed class AdminUsers(IdentityDbContext db, DeliveryDbContext delivery, UserManager<ApplicationUser> users,
    AuthDatabaseLock locks, AuthCryptography crypto, TimeProvider clock) : IAdminUsers
{
    public const string RoleClaim = "velo.account.role";
    private static readonly string[] Roles = ["Customer", "Admin", "Driver", "Crew"];

    public async Task<AdminUserPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 || search?.Length > 100)
            throw new AuthException(400, "invalid_user_query");
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => (x.Name != null && x.Name.Contains(term)) ||
                (x.Email != null && x.Email.Contains(term)) || x.PhoneNumber!.Contains(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var claims = await db.UserClaims.AsNoTracking().Where(x => ids.Contains(x.UserId) &&
            (x.ClaimType == RoleClaim || x.ClaimType == StaffAccessService.ClaimType)).ToArrayAsync(ct);
        var drivers = await delivery.Drivers.AsNoTracking().Where(x => ids.Contains(x.UserId) && x.IsActive).Select(x => x.UserId).ToArrayAsync(ct);
        var crew = await delivery.CrewMembers.AsNoTracking().Where(x => ids.Contains(x.UserId) && x.IsActive).Select(x => x.UserId).ToArrayAsync(ct);
        return new(rows.Select(u => Map(u, Resolve(claims.Where(c => c.UserId == u.Id), drivers.Contains(u.Id), crew.Contains(u.Id)))).ToArray(), page, pageSize, count);
    }

    public Task<AdminUserResponse> CreateAsync(Guid actor, CreateAdminUserRequest request, CancellationToken ct)
    {
        var (name, email) = Profile(request.Name, request.Email);
        var role = Role(request.Role);
        var phone = PhoneNumber.Normalize(request.PhoneNumber);
        var id = Guid.NewGuid();
        return Run(["user:" + id.ToString("N"), "phone:" + crypto.HashPrivate(phone)], async () =>
        {
            if (await db.Users.AnyAsync(x => x.PhoneNumber == phone, ct)) throw new AuthException(409, "phone_already_registered");
            var user = new ApplicationUser { Id = id, UserName = phone, PhoneNumber = phone, Name = name, Email = email,
                IsActive = request.IsActive, PhoneNumberConfirmed = false };
            Check(await users.CreateAsync(user));
            Check(await users.AddToRoleAsync(user, "Customer"));
            await ApplyRole(user, role, null, ct);
            await db.SaveChangesAsync(ct); await delivery.SaveChangesAsync(ct);
            return Map(user, role);
        }, ct);
    }

    public Task<AdminUserResponse> UpdateAsync(Guid actor, Guid id, AdminUserRequest request, CancellationToken ct)
    {
        var (name, email) = Profile(request.Name, request.Email);
        if (request.Role is not null) Role(request.Role);
        return Run(["user:" + id.ToString("N")], async () =>
        {
            var user = await users.FindByIdAsync(id.ToString()) ?? throw new AuthException(404, "user_not_found");
            if (string.IsNullOrEmpty(request.Version) || request.Version != user.ConcurrencyStamp)
                throw new AuthException(409, "user_version_conflict");
            var claims = await db.UserClaims.Where(x => x.UserId == id).ToArrayAsync(ct);
            await FleetLocks(["user:" + id.ToString("N"), "crew-user:" + id.ToString("N")], ct);
            var driver = await delivery.Drivers.SingleOrDefaultAsync(x => x.UserId == id, ct);
            var crew = await delivery.CrewMembers.SingleOrDefaultAsync(x => x.UserId == id, ct);
            var currentRole = Resolve(claims, driver?.IsActive == true, crew?.IsActive == true);
            var role = request.Role ?? currentRole;
            if (actor == id && !request.IsActive) throw new AuthException(400, "cannot_deactivate_self");
            if (actor == id && role != currentRole) throw new AuthException(400, "cannot_change_own_role");
            var fleetKeys = new List<string> { "fleet-user:" + id.ToString("N") };
            if (driver is not null) fleetKeys.Add("driver:" + driver.Id.ToString("N"));
            if (crew is not null) fleetKeys.Add("crew:" + crew.Id.ToString("N"));
            await FleetLocks(fleetKeys, ct);
            var losingDriver = driver is not null && (!request.IsActive || request.Role is not null && role != "Driver");
            var losingCrew = crew is not null && (!request.IsActive || request.Role is not null && role != "Crew");
            if ((losingDriver || losingCrew) && await delivery.VanDispatches.AnyAsync(x => x.EndedAt == null &&
                (losingDriver && x.Driver.UserId == id || losingCrew && x.CrewMember.UserId == id), ct))
                throw new AuthException(409, "user_has_active_dispatch");
            if (losingDriver && await delivery.Shipments.AnyAsync(x => x.Driver.UserId == id &&
                (x.Status == "Assigned" || x.Status == "OutForDelivery" || x.Status == "Failed"), ct))
                throw new AuthException(409, "user_has_active_delivery");
            var changedAccess = user.IsActive != request.IsActive || role != currentRole;
            user.Name = name; user.IsActive = request.IsActive;
            if (user.Email != email) user.EmailConfirmed = false;
            user.Email = email;
            Check(await users.UpdateAsync(user));
            if (request.Role is not null) await ApplyRole(user, role, currentRole, ct);
            else
            {
                if (driver is not null) { driver.Name = name; if (!user.IsActive) driver.IsActive = false; }
                if (crew is not null) { crew.Name = name; if (!user.IsActive) crew.IsActive = false; }
            }
            if (changedAccess)
            {
                Check(await users.UpdateSecurityStampAsync(user));
                await db.AuthSessions.Where(x => x.UserId == id && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);
            }
            await db.SaveChangesAsync(ct); await delivery.SaveChangesAsync(ct);
            return Map(user, role);
        }, ct);
    }

    private async Task ApplyRole(ApplicationUser user, string role, string? previous, CancellationToken ct)
    {
        db.UserClaims.RemoveRange(await db.UserClaims.Where(x => x.UserId == user.Id && x.ClaimType == RoleClaim).ToArrayAsync(ct));
        db.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = user.Id, ClaimType = RoleClaim, ClaimValue = role });
        if (role != previous)
        {
            db.UserClaims.RemoveRange(await db.UserClaims.Where(x => x.UserId == user.Id && x.ClaimType == StaffAccessService.ClaimType).ToArrayAsync(ct));
            string[] permissions = role == "Admin" ? StaffAccessService.Allowed : role == "Crew" ? ["orders.manage"] : [];
            db.UserClaims.AddRange(permissions.Select(p => new IdentityUserClaim<Guid> { UserId = user.Id, ClaimType = StaffAccessService.ClaimType, ClaimValue = p }));
        }
        var driver = await delivery.Drivers.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        var crew = await delivery.CrewMembers.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (role == "Driver" && driver is null)
        { driver = new Driver { Id = Guid.NewGuid(), UserId = user.Id, Name = user.Name! }; delivery.Drivers.Add(driver); }
        if (role == "Crew" && crew is null)
        { crew = new CrewMember { Id = Guid.NewGuid(), UserId = user.Id, Name = user.Name! }; delivery.CrewMembers.Add(crew); }
        if (driver is not null) { driver.Name = user.Name!; driver.IsActive = user.IsActive && role == "Driver"; }
        if (crew is not null) { crew.Name = user.Name!; crew.IsActive = user.IsActive && role == "Crew"; }
    }

    private Task<T> Run<T>(string[] keys, Func<Task<T>> action, CancellationToken ct) => locks.ExecuteAsync(keys, async () =>
    {
        delivery.ChangeTracker.Clear();
        delivery.Database.SetDbConnection(db.Database.GetDbConnection(), contextOwnsConnection: false);
        await delivery.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction(), ct);
        try { return await action(); }
        finally { await delivery.Database.UseTransactionAsync(null, CancellationToken.None); }
    }, ct);

    private async Task FleetLocks(IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
        {
            var resource = "velo:delivery:" + key;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Fleet account lock unavailable.', 1;
                """, ct);
        }
    }
    private static string Resolve(IEnumerable<IdentityUserClaim<Guid>> claims, bool driver, bool crew)
    {
        var selected = claims.FirstOrDefault(x => x.ClaimType == RoleClaim)?.ClaimValue;
        if (selected is not null && Roles.Contains(selected)) return selected;
        var permissions = claims.Where(x => x.ClaimType == StaffAccessService.ClaimType).Select(x => x.ClaimValue).ToArray();
        if (permissions.Any(p => p != "orders.manage")) return "Admin";
        return crew ? "Crew" : driver ? "Driver" : permissions.Length > 0 ? "Admin" : "Customer";
    }
    private static string Role(string? role) => role is not null && Roles.Contains(role) ? role : throw new AuthException(400, "invalid_user_role");
    private static (string Name, string Email) Profile(string? name, string? email)
    {
        name = name?.Trim(); email = email?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 160 || name.Any(char.IsControl) || string.IsNullOrEmpty(email) ||
            email.Length > 256 || email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || !new EmailAddressAttribute().IsValid(email))
            throw new AuthException(400, "invalid_user_profile");
        return (name, email);
    }
    private static void Check(IdentityResult result)
    { if (!result.Succeeded) throw new AuthException(409, "user_update_conflict"); }
    private static AdminUserResponse Map(ApplicationUser user, string role) => new(user.Id, user.Name, user.Email,
        user.PhoneNumber, user.IsActive, user.PhoneNumberConfirmed, user.ConcurrencyStamp!, role);
}
