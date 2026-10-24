using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Customers.Infrastructure.Persistence;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Orders.Application;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Velo.Modules.Payments.Infrastructure.Persistence;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Notifications.Infrastructure.Persistence;
namespace Velo.Api.Infrastructure;

// Composition-root adapter: modules exchange contracts, while this deployment
// coordinates their persistence through one physical SQL connection/transaction.
public sealed class OrderSqlTransaction : IOrderTransaction, IAsyncDisposable, IDisposable
{
    private readonly OrdersDbContext orders;
    private readonly DbContext[] contexts;
    private readonly SqlConnection connection;
    public OrderSqlTransaction(OrdersDbContext orders, InventoryDbContext inventory, CustomersDbContext customers, CatalogDbContext catalog,
        PaymentsDbContext payments, DeliveryDbContext delivery, NotificationsDbContext notifications)
    {
        this.orders = orders;
        contexts = [orders, inventory, customers, catalog, payments, delivery, notifications];
        var configured = new SqlConnectionStringBuilder(orders.Database.GetConnectionString());
        foreach (var db in contexts)
        {
            var candidate = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
            if (candidate.InitialCatalog != configured.InitialCatalog || candidate.DataSource != configured.DataSource)
                throw new InvalidOperationException("Order transactions require all module contexts to use the same SQL Server database.");
            if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Cannot replace an active module connection.");
        }
        connection = new SqlConnection(configured.ConnectionString);
        foreach (var db in contexts) db.Database.SetDbConnection(connection, contextOwnsConnection: false);
    }
    public Task<T> RunAsync<T>(Guid customer, Guid operation, Func<Task<T>> action, CancellationToken ct) =>
        orders.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            foreach (var db in contexts) db.ChangeTracker.Clear();
            await using var transaction = await orders.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var db in contexts.Skip(1)) await db.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
                foreach (var key in new[] { "velo:orders:cart:" + customer.ToString("N"), "velo:orders:operation:" + operation.ToString("N") })
                    await orders.Database.ExecuteSqlInterpolatedAsync($"""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                        IF @result < 0 THROW 51001, 'Order lock unavailable.', 1;
                        """, ct);
                var result = await action();
                await orders.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            finally
            {
                // Detach borrowed transactions before disposing the owning transaction.
                foreach (var db in contexts.Skip(1)) await db.Database.UseTransactionAsync(null, CancellationToken.None);
            }
        });
    public ValueTask DisposeAsync() => connection.DisposeAsync();
    public void Dispose() => connection.Dispose();
}
