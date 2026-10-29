using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Infrastructure.Persistence;
namespace Velo.Modules.Inventory.Infrastructure;

public static class InventoryModuleExtensions
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services,
        string connectionString, IConfiguration configuration)
    {
        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema);
            sql.EnableRetryOnFailure();
        }));
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<InventoryOptions>(configuration.GetSection("Inventory"));
        services.AddScoped<InventoryTransaction>();
        services.AddScoped<InventoryService>();
        services.AddScoped<Velo.Modules.Inventory.Contracts.IMobileWarehouses, MobileWarehouses>();
        services.AddScoped<Velo.Modules.Inventory.Contracts.IOrderStock, OrderStock>();
        services.AddHostedService<InventoryExpiryWorker>();
        services.AddAuthorization(options => options.AddPolicy(InventoryPolicies.Manage, policy =>
            policy.RequireAuthenticatedUser().RequireClaim("permission", InventoryPolicies.Permission)));
        return services;
    }
}
