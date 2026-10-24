using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Velo.Api.Configuration;
using Xunit;

namespace Velo.Auth.IntegrationTests;

public sealed class DevelopmentSettingsTests
{
    [Fact]
    public void Missing_development_settings_are_loaded_without_overwriting_existing_values()
    {
        using var local = new LocalSettings("Development");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Velo"] = "existing-connection"
        });
        DevelopmentSettings.ApplyFallback(configuration, local.Environment);
        Assert.Equal("existing-connection", configuration.GetConnectionString("Velo"));
        Assert.Equal("local-test-key", configuration["Authentication:OtpHashKey"]);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Local_settings_are_not_loaded_outside_development(string environment)
    {
        using var local = new LocalSettings(environment);
        var configuration = new ConfigurationManager();
        DevelopmentSettings.ApplyFallback(configuration, local.Environment);
        Assert.Null(configuration.GetConnectionString("Velo"));
        Assert.Null(configuration["Authentication:OtpHashKey"]);
    }

    private sealed class LocalSettings : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "velo-config-tests-" + Guid.NewGuid().ToString("N"));
        public IHostEnvironment Environment { get; }
        public LocalSettings(string environment)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "appsettings.Local.json"),
                """{"ConnectionStrings":{"Velo":"local-test-connection"},"Authentication":{"OtpHashKey":"local-test-key"}}""");
            Environment = new TestEnvironment { EnvironmentName = environment, ContentRootPath = root };
        }
        public void Dispose()
        {
            File.Delete(Path.Combine(root, "appsettings.Local.json"));
            Directory.Delete(root);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "Velo.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
