using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Velo.Modules.Identity.Infrastructure;
using Xunit;

namespace Velo.Auth.IntegrationTests;

public sealed class DevelopmentSafetyTests
{
    [Fact]
    public async Task Development_sender_refuses_to_run_in_production()
    {
        var sender = new DevelopmentOtpSender(new ProductionEnvironment());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            "+201001234567", "123456", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Production_without_key_protection_fails_closed()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() => services.AddIdentityModule(
            "Server=unused;Database=unused", new ConfigurationBuilder().Build(), new ProductionEnvironment()));
        Assert.Contains("Data Protection certificate", error.Message);
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Velo.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
