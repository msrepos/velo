using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Velo.Modules.Catalog.Application;
using Velo.Modules.Catalog.Infrastructure.Persistence;
using Velo.Modules.Inventory.Infrastructure.Persistence;
using Velo.Modules.Customers.Infrastructure.Persistence;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Velo.Modules.Payments.Infrastructure.Persistence;
using Velo.Modules.Delivery.Infrastructure.Persistence;
using Velo.Modules.Notifications.Infrastructure.Persistence;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Velo.Catalog.IntegrationTests;

public sealed class CatalogFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private readonly string databaseName = "velo_catalog_tests_" + Guid.NewGuid().ToString("N");
    private string masterConnection = "";
    private bool created;
    private string staffToken = "";
    private string customerToken = "";

    public async ValueTask InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
        var sql = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VELO_TEST_SQL_CONNECTION")
            ?? configuration.GetConnectionString("Velo")
            ?? throw new InvalidOperationException("Configure local test SQL Server credentials."))
        { InitialCatalog = "master", Pooling = false };
        masterConnection = sql.ConnectionString;
        await using (var connection = new SqlConnection(masterConnection))
        {
            await connection.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{databaseName}]", connection).ExecuteNonQueryAsync();
            created = true;
        }
        sql.InitialCatalog = databaseName;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Velo"] = sql.ConnectionString,
                    ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:SmsProvider"] = "Development",
                    ["Authentication:ExposeDevelopmentCodes"] = "true",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Inventory:EnableExpiryWorker"] = "false",
                    ["Orders:EnableExpiryWorker"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                ReplaceContext<IdentityDbContext>(services, sql.ConnectionString, IdentityDbContext.Schema);
                ReplaceContext<CatalogDbContext>(services, sql.ConnectionString, CatalogDbContext.Schema);
                ReplaceContext<InventoryDbContext>(services, sql.ConnectionString, InventoryDbContext.Schema);
                ReplaceContext<CustomersDbContext>(services, sql.ConnectionString, CustomersDbContext.Schema);
                ReplaceContext<OrdersDbContext>(services, sql.ConnectionString, OrdersDbContext.Schema);
                ReplaceContext<PaymentsDbContext>(services, sql.ConnectionString, PaymentsDbContext.Schema);
                ReplaceContext<DeliveryDbContext>(services, sql.ConnectionString, DeliveryDbContext.Schema);
                ReplaceContext<NotificationsDbContext>(services, sql.ConnectionString, NotificationsDbContext.Schema);
            });
        });

        using var scope = Factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        foreach (var db in new DbContext[] { identity, catalog, inventory,
            scope.ServiceProvider.GetRequiredService<CustomersDbContext>(), scope.ServiceProvider.GetRequiredService<OrdersDbContext>(), scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(),
            scope.ServiceProvider.GetRequiredService<DeliveryDbContext>(), scope.ServiceProvider.GetRequiredService<NotificationsDbContext>() })
        {
            if (db.Database.GetDbConnection().Database != databaseName)
                throw new InvalidOperationException("Refusing to write outside the dedicated test database.");
            await db.Database.MigrateAsync();
        }
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "+201099999999", PhoneNumber = "+201099999999",
            PhoneNumberConfirmed = true
        };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("Failed to set up test identity.");
        var session = new AuthSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!,
            RefreshTokenHash = "", CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        customerToken = scope.ServiceProvider.GetRequiredService<SessionTokens>().Issue(user, session).AccessToken;
        identity.AuthSessions.Add(session);
        await identity.SaveChangesAsync();
        staffToken = (await scope.ServiceProvider.GetRequiredService<DevelopmentStaffAccess>()
            .IssueAsync(user.Id, [CatalogPolicies.Permission], CancellationToken.None)).AccessToken;
    }

    private static void ReplaceContext<T>(IServiceCollection services, string connection, string schema) where T : DbContext
    {
        services.RemoveAll<T>();
        services.RemoveAll<DbContextOptions<T>>();
        services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        services.AddDbContext<T>(options => options.UseSqlServer(connection, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            sql.EnableRetryOnFailure();
        }));
    }

    public HttpClient Client(string? access = null)
    {
        var client = Factory.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        if (access is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                access == "staff" ? staffToken : customerToken);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (created && System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^velo_catalog_tests_[a-f0-9]{32}$"))
        {
            await using var connection = new SqlConnection(masterConnection);
            await connection.OpenAsync();
            await new SqlCommand($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", connection)
                .ExecuteNonQueryAsync();
        }
    }
}
