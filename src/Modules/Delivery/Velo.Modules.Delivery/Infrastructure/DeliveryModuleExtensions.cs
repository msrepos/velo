using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Delivery.Contracts;
using Velo.Modules.Delivery.Infrastructure.Persistence;
namespace Velo.Modules.Delivery.Infrastructure;
public static class DeliveryModuleExtensions
{
    public static IServiceCollection AddDeliveryModule(this IServiceCollection services, string connection)
    {
        services.AddDbContext<DeliveryDbContext>(o => o.UseSqlServer(connection, sql =>
        { sql.MigrationsHistoryTable("__EFMigrationsHistory", DeliveryDbContext.Schema); sql.EnableRetryOnFailure(); }));
        services.AddScoped<DeliveryService>(); services.AddScoped<IDeliveries>(sp => sp.GetRequiredService<DeliveryService>());
        services.AddAuthorization(o => o.AddPolicy(DeliveryPolicies.Manage, p => p.RequireAuthenticatedUser().RequireClaim("permission", DeliveryPolicies.Permission)));
        return services;
    }
}
