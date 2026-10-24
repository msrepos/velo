using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Payments.Application;
using Velo.Modules.Payments.Contracts;
using Velo.Modules.Payments.Infrastructure.Persistence;
namespace Velo.Modules.Payments.Infrastructure;
public static class PaymentsModuleExtensions
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, string connection)
    {
        services.AddDbContext<PaymentsDbContext>(o => o.UseSqlServer(connection, sql =>
        { sql.MigrationsHistoryTable("__EFMigrationsHistory", PaymentsDbContext.Schema); sql.EnableRetryOnFailure(); }));
        services.AddScoped<PaymentService>(); services.AddScoped<ICodPayments>(sp => sp.GetRequiredService<PaymentService>());
        services.AddAuthorization(o => o.AddPolicy(PaymentPolicies.Manage, p => p.RequireAuthenticatedUser().RequireClaim("permission", PaymentPolicies.Permission)));
        return services;
    }
}
