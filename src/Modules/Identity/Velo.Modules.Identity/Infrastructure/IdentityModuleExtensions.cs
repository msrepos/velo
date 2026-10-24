using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure.Persistence;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;

namespace Velo.Modules.Identity.Infrastructure;

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services, string connectionString,
        IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema);
                sql.EnableRetryOnFailure();
            }));

        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.Section))
            .ValidateDataAnnotations()
            .Validate(AuthOptions.HasValidHashKey, "Authentication:OtpHashKey must be a base64 key of at least 32 bytes.")
            .Validate(x => environment.IsDevelopment() || !x.ExposeDevelopmentCodes,
                "Development OTP codes cannot be exposed outside Development.")
            .Validate(x => environment.IsDevelopment() || x.SmsProvider != "Development",
                "The development SMS provider cannot run outside Development.")
            .Validate(x => x.SmsProvider is "Development" or "HttpGateway", "Configure a supported SMS provider before starting authentication.")
            .Validate(AuthOptions.HasValidGateway, "Configure HTTPS Authentication:GatewayUrl and GatewayApiKey.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuthCryptography>();
        services.AddScoped<AuthDatabaseLock>();
        services.AddScoped<AuthService>();
        services.AddScoped<SessionTokens>();
        services.AddScoped<DevelopmentStaffAccess>();
        services.AddScoped<StaffAccessService>();
        services.AddScoped<Velo.Modules.Identity.Contracts.IVerifiedUsers, VerifiedUsers>();
        if (environment.IsDevelopment() && configuration["Authentication:SmsProvider"] == "Development")
            services.AddScoped<IOtpSender, DevelopmentOtpSender>();
        else if (configuration["Authentication:SmsProvider"] == "HttpGateway")
            services.AddHttpClient<IOtpSender, HttpGatewayOtpSender>(client => client.Timeout = TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        var protection = services.AddDataProtection().SetApplicationName("Velo.Identity")
            .PersistKeysToDbContext<IdentityDbContext>();
        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
            protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath, configuration["DataProtection:CertificatePassword"]));
        else if (!environment.IsDevelopment())
            throw new InvalidOperationException("Configure a Data Protection certificate outside Development.");

        services.AddAuthentication(SessionTokens.Scheme)
            .AddBearerToken(IdentityConstants.BearerScheme, bearer =>
            {
                var settings = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new();
                bearer.BearerTokenExpiration = TimeSpan.FromMinutes(settings.AccessTokenMinutes);
                bearer.RefreshTokenExpiration = TimeSpan.FromDays(settings.SessionDays);
            })
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionTokens.Scheme, null);
        services.AddAuthorization();
        services.AddOptions<BearerTokenOptions>(IdentityConstants.BearerScheme)
            .Configure<TimeProvider>((bearer, clock) => bearer.TimeProvider = clock);

        services.AddIdentityCore<ApplicationUser>(options => options.Stores.MaxLengthForKeys = 128)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityDbContext>();

        return services;
    }
}
