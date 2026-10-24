using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Velo.Modules.Customers.Infrastructure.Persistence;

namespace Velo.Modules.Customers.Infrastructure;

public static class CustomersModuleExtensions
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services, string connection)
    {
        services.AddDbContext<CustomersDbContext>(options => options.UseSqlServer(connection, sql =>
        { sql.MigrationsHistoryTable("__EFMigrationsHistory", CustomersDbContext.Schema); sql.EnableRetryOnFailure(); }));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AddressService>();
        services.AddScoped<Velo.Modules.Customers.Contracts.ICustomerAddresses, CustomerAddressLookup>();
        return services;
    }
}
