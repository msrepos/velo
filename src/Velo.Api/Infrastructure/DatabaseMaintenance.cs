using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Customers.Infrastructure.Persistence;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Payments.Infrastructure.Persistence;
using Velo.Modules.Notifications.Infrastructure.Persistence;
namespace Velo.Api.Infrastructure;
public static class DatabaseMaintenance
{
    public static DbContext[] Contexts(IServiceProvider services) =>
    [services.GetRequiredService<IdentityDbContext>(), services.GetRequiredService<CatalogDbContext>(),
     services.GetRequiredService<CustomersDbContext>(), services.GetRequiredService<InventoryDbContext>(),
     services.GetRequiredService<OrdersDbContext>(), services.GetRequiredService<DeliveryDbContext>(),
     services.GetRequiredService<PaymentsDbContext>(), services.GetRequiredService<NotificationsDbContext>()];
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        foreach (var db in Contexts(services)) await db.Database.MigrateAsync(ct);
    }
}
public sealed class DatabaseReadinessCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            foreach (var db in DatabaseMaintenance.Contexts(scope.ServiceProvider))
                if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return HealthCheckResult.Unhealthy("Database migrations are pending.");
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { return HealthCheckResult.Unhealthy("Database is unavailable."); }
    }
}
