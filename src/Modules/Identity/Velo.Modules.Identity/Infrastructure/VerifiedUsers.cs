using Microsoft.EntityFrameworkCore;
using Velo.Modules.Identity.Contracts;
using Velo.Modules.Identity.Infrastructure.Persistence;
namespace Velo.Modules.Identity.Infrastructure;
public sealed class VerifiedUsers(IdentityDbContext db) : IVerifiedUsers
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive && x.PhoneNumberConfirmed, ct);
}
