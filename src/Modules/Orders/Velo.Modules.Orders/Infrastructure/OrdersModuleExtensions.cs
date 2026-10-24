using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Velo.Modules.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Velo.Modules.Orders.Application;

namespace Velo.Modules.Orders.Infrastructure;

public static class OrdersModuleExtensions
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, string connection, IConfiguration configuration)
    {
        services.AddDbContext<OrdersDbContext>(options => options.UseSqlServer(connection, sql =>
        { sql.MigrationsHistoryTable("__EFMigrationsHistory", OrdersDbContext.Schema); sql.EnableRetryOnFailure(); }));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CartService>();
        services.AddScoped<OrderService>();
        services.AddOptions<OrderOptions>().Bind(configuration.GetSection("Orders"))
            .Validate(x => !x.EnableCheckout || (double.IsFinite(x.DeliveryRadiusKm) && x.DeliveryRadiusKm is > 0 and <= 50 &&
                x.DeliveryFee is >= 0 and <= 1000 && decimal.Round(x.DeliveryFee, 2) == x.DeliveryFee &&
                x.ReservationMinutes is >= 1 and <= 30), "Configure valid checkout delivery fee, radius and reservation lifetime.")
            .ValidateOnStart();
        services.AddHostedService<OrderExpiryWorker>();
        services.AddAuthorization(options => options.AddPolicy(OrderPolicies.Manage,
            policy => policy.RequireAuthenticatedUser().RequireClaim("permission", OrderPolicies.Permission)));
        return services;
    }
}
