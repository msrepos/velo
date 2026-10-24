using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Application;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Customers.Infrastructure.Persistence;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Velo.Modules.Payments.Infrastructure.Persistence;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Notifications.Infrastructure.Persistence;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Velo.Auth.IntegrationTests;

public sealed class TestClock : TimeProvider
{
    private long ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan amount) => Interlocked.Add(ref ticks, amount.Ticks);
}

public sealed class AuthFixture : IAsyncLifetime
{
    public TestClock Clock { get; } = new();
    public TestSmsSender Sms { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private readonly string databaseName = "velo_auth_tests_" + Guid.NewGuid().ToString("N");
    private string masterConnection = "";
    private bool createdDatabase;

    public async ValueTask InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
        var connection = Environment.GetEnvironmentVariable("VELO_TEST_SQL_CONNECTION")
            ?? config.GetConnectionString("Velo")
            ?? throw new InvalidOperationException("Set VELO_TEST_SQL_CONNECTION or the Velo.Api user secret.");
        var sql = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", Pooling = false };
        masterConnection = sql.ConnectionString;
        await using (var master = new SqlConnection(masterConnection))
        {
            await master.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{databaseName}]", master).ExecuteNonQueryAsync();
            createdDatabase = true;
        }

        sql.InitialCatalog = databaseName;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Velo"] = sql.ConnectionString,
                    ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:SmsProvider"] = "Development",
                    ["Authentication:ExposeDevelopmentCodes"] = "true",
                    ["Authentication:OtpRequestsPerClientPerHour"] = "1000",
                    ["Authentication:OtpVerificationsPerClientPerHour"] = "1000",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Inventory:EnableExpiryWorker"] = "false",
                    ["Orders:EnableExpiryWorker"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                // The host captures the production connection while registering its
                // module. Replace every context options callback, not just configuration.
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(
                    sql.ConnectionString, provider =>
                    {
                        provider.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema);
                        provider.EnableRetryOnFailure();
                    }));
                services.RemoveAll<CatalogDbContext>();
                services.RemoveAll<DbContextOptions<CatalogDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CatalogDbContext>>();
                services.AddDbContext<CatalogDbContext>(options => options.UseSqlServer(
                    sql.ConnectionString, provider =>
                    {
                        provider.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema);
                        provider.EnableRetryOnFailure();
                    }));
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<InventoryDbContext>();
                services.RemoveAll<DbContextOptions<InventoryDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
                services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(
                    sql.ConnectionString, provider =>
                    {
                        provider.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema);
                        provider.EnableRetryOnFailure();
                    }));
                ReplaceContext<CustomersDbContext>(services, sql.ConnectionString, CustomersDbContext.Schema);
                ReplaceContext<OrdersDbContext>(services, sql.ConnectionString, OrdersDbContext.Schema);
                ReplaceContext<PaymentsDbContext>(services, sql.ConnectionString, PaymentsDbContext.Schema);
                ReplaceContext<DeliveryDbContext>(services, sql.ConnectionString, DeliveryDbContext.Schema);
                ReplaceContext<NotificationsDbContext>(services, sql.ConnectionString, NotificationsDbContext.Schema);
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IOtpSender>();
                services.AddSingleton<IOtpSender>(Sms);
            });
        });
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (context.Database.GetDbConnection().Database != databaseName)
            throw new InvalidOperationException("Refusing to run auth tests against a non-test database.");
        await context.Database.MigrateAsync();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (catalog.Database.GetDbConnection().Database != databaseName)
            throw new InvalidOperationException("Refusing to use Catalog outside the dedicated test database.");
        await catalog.Database.MigrateAsync();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        if (inventory.Database.GetDbConnection().Database != databaseName)
            throw new InvalidOperationException("Refusing to use Inventory outside the dedicated test database.");
        await inventory.Database.MigrateAsync();
        foreach (var db in new DbContext[] { scope.ServiceProvider.GetRequiredService<CustomersDbContext>(),
            scope.ServiceProvider.GetRequiredService<OrdersDbContext>(), scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(),
            scope.ServiceProvider.GetRequiredService<DeliveryDbContext>(), scope.ServiceProvider.GetRequiredService<NotificationsDbContext>() })
        {
            if (db.Database.GetDbConnection().Database != databaseName)
                throw new InvalidOperationException("Refusing to write outside the dedicated test database.");
            await db.Database.MigrateAsync();
        }
    }

    private static void ReplaceContext<T>(IServiceCollection services, string connection, string schema) where T : DbContext
    {
        services.RemoveAll<T>();
        services.RemoveAll<DbContextOptions<T>>();
        services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        services.AddDbContext<T>(options => options.UseSqlServer(connection, sql =>
        { sql.MigrationsHistoryTable("__EFMigrationsHistory", schema); sql.EnableRetryOnFailure(); }));
    }

    public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("http://localhost")
    });

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        // Only the uniquely named database created by this fixture may be dropped.
        if (createdDatabase && System.Text.RegularExpressions.Regex.IsMatch(
            databaseName, "^velo_auth_tests_[a-f0-9]{32}$"))
        {
            await using var master = new SqlConnection(masterConnection);
            await master.OpenAsync();
            await new SqlCommand($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", master)
                .ExecuteNonQueryAsync();
        }
    }
}

public sealed class TestSmsSender : IOtpSender
{
    private int failNext;
    public void FailNext() => Interlocked.Exchange(ref failNext, 1);
    public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref failNext, 0) == 1)
            throw new InvalidOperationException("Simulated provider outage.");
        return Task.CompletedTask;
    }
}
