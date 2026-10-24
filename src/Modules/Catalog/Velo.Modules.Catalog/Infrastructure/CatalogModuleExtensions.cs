using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Velo.Modules.Catalog.Application;
using Velo.Modules.Catalog.Infrastructure.Persistence;

namespace Velo.Modules.Catalog.Infrastructure;

public static class CatalogModuleExtensions
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogDbContext>(options => options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema);
            sql.EnableRetryOnFailure();
        }));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CatalogService>();
        services.AddScoped<Velo.Modules.Catalog.Contracts.ICatalogVariants, CatalogVariantLookup>();
        services.AddScoped<Velo.Modules.Catalog.Contracts.ICatalogOffers, CatalogOfferLookup>();
        services.AddAuthorization(options => options.AddPolicy(CatalogPolicies.Manage,
            policy => policy.RequireAuthenticatedUser().RequireClaim("permission", CatalogPolicies.Permission)));
        return services;
    }
}
