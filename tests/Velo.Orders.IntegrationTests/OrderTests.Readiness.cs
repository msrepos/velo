using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.Modules.Notifications.Infrastructure.Persistence;
using Xunit;
namespace Velo.Orders.IntegrationTests;
public sealed partial class OrderTests
{
    [Fact]
    public async Task Readiness_checks_schema_while_liveness_remains_healthy_with_pending_migrations()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", Ct)).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var id = (await db.Database.GetAppliedMigrationsAsync(Ct)).Last();
        var version = await db.Database.SqlQueryRaw<string>("SELECT [ProductVersion] AS [Value] FROM notifications.__EFMigrationsHistory WHERE MigrationId = {0}", id).SingleAsync(Ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM notifications.__EFMigrationsHistory WHERE MigrationId={id}", Ct);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Ct)).StatusCode);
        }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notifications.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({id},{version})", Ct); }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", Ct)).StatusCode);
    }
}
