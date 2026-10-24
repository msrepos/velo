using Microsoft.EntityFrameworkCore;
using Velo.Modules.Identity.Infrastructure.Persistence;

namespace Velo.Modules.Identity.Infrastructure;

// SQL application locks serialize conflicting operations across all API instances.
// Locks are released by transaction commit/rollback, including on failed requests.
public sealed class AuthDatabaseLock(IdentityDbContext db)
{
    public async Task<T> ExecuteAsync<T>(IEnumerable<string> keys, Func<Task<T>> action, CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
            {
                var resource = "velo:auth:" + key;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock
                        @Resource={resource}, @LockMode=N'Exclusive',
                        @LockOwner=N'Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 51000, 'Authentication operation could not acquire its lock.', 1;
                    """, ct);
            }

            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
