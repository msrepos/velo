using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Notifications.Contracts;
using Velo.Modules.Notifications.Infrastructure.Persistence;
namespace Velo.Modules.Notifications.Infrastructure;
public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connection)
    {
        services.AddDbContext<NotificationsDbContext>(o => o.UseSqlServer(connection, sql =>
        { sql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema); sql.EnableRetryOnFailure(); }));
        services.AddScoped<NotificationService>();
        services.AddScoped<IInAppNotifications>(sp => sp.GetRequiredService<NotificationService>());
        return services;
    }
}
