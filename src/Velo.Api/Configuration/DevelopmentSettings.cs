using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Velo.Api.Configuration;

public static class DevelopmentSettings
{
    public static void ApplyFallback(ConfigurationManager configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;

        // Visual Studio and CLI can resolve User Secrets under different profiles.
        // An ignored local file fills missing values only; configured values win.
        foreach (var root in new[] { environment.ContentRootPath, AppContext.BaseDirectory }.Distinct())
        {
            using var local = new ConfigurationManager();
            local.SetBasePath(root).AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
            var fallback = new Dictionary<string, string?>();
            foreach (var key in new[] { "ConnectionStrings:Velo", "Authentication:OtpHashKey" })
            {
                if (string.IsNullOrWhiteSpace(configuration[key]) && !string.IsNullOrWhiteSpace(local[key]))
                    fallback[key] = local[key];
            }
            if (fallback.Count > 0) configuration.AddInMemoryCollection(fallback);
        }
    }
}
