using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Infrastructure.Persistence;
namespace Velo.Modules.Inventory.Infrastructure;

public sealed class InventoryTransaction(InventoryDbContext db)
{
    public Task<T> RunAsync<T>(IEnumerable<string> keys, Func<Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return ApplyAsync(keys, action, ct);
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await ApplyAsync(keys, action, ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    private async Task<T> ApplyAsync<T>(IEnumerable<string> keys, Func<Task<T>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
        {
            var resource = "velo:inventory:" + key;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive',
                    @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Inventory lock unavailable.', 1;
                """, ct);
        }
        var result = await action();
        await db.SaveChangesAsync(ct);
        return result;
    }
}
