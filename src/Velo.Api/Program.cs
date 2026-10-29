using Velo.Modules.Identity.Infrastructure;
using Velo.Modules.Identity.Endpoints;
using Velo.Modules.Catalog.Infrastructure;
using Velo.Modules.Catalog.Endpoints;
using Velo.Modules.Catalog.Application;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Velo.Api.Configuration;
using Velo.Modules.Inventory.Infrastructure;
using Velo.Modules.Inventory.Endpoints;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Customers.Infrastructure;
using Velo.Modules.Customers.Endpoints;
using Velo.Modules.Orders.Infrastructure;
using Velo.Modules.Orders.Endpoints;
using Velo.Modules.Orders.Application;
using Velo.Api.Infrastructure;
using Velo.Modules.Payments.Infrastructure;
using Velo.Modules.Payments.Endpoints;
using Velo.Modules.Payments.Application;
using Velo.Modules.Delivery.Infrastructure;
using Velo.Modules.Delivery.Endpoints;
using Velo.Modules.Delivery.Application;
using Velo.Modules.Notifications.Infrastructure;
using Velo.Modules.Notifications.Endpoints;

var builder = WebApplication.CreateBuilder(args);
DevelopmentSettings.ApplyFallback(builder.Configuration, builder.Environment);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
builder.Services.AddIdentityModule(
    builder.Configuration.GetConnectionString("Velo")
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:Velo is missing (environment: {builder.Environment.EnvironmentName}). " +
        "For local Visual Studio debugging select the http launch profile and configure Velo.Api User Secrets."),
    builder.Configuration, builder.Environment);
builder.Services.AddCatalogModule(builder.Configuration.GetConnectionString("Velo")!);
builder.Services.AddInventoryModule(builder.Configuration.GetConnectionString("Velo")!, builder.Configuration);
builder.Services.AddCustomersModule(builder.Configuration.GetConnectionString("Velo")!);
builder.Services.AddOrdersModule(builder.Configuration.GetConnectionString("Velo")!, builder.Configuration);
builder.Services.AddScoped<IOrderTransaction, OrderSqlTransaction>();
builder.Services.AddScoped<Velo.Modules.Identity.Application.IAdminUsers, AdminUsers>();
builder.Services.AddPaymentsModule(builder.Configuration.GetConnectionString("Velo")!);
builder.Services.AddDeliveryModule(builder.Configuration.GetConnectionString("Velo")!);
builder.Services.AddNotificationsModule(builder.Configuration.GetConnectionString("Velo")!);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = error => error is BadHttpRequestException requestError
        ? requestError.StatusCode : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentityEndpoints();
app.MapUserAdminEndpoints();
app.MapCatalogEndpoints();
app.MapInventoryEndpoints();
app.MapCustomerEndpoints();
app.MapCartEndpoints();
app.MapOrderEndpoints();
app.MapPaymentEndpoints();
app.MapDeliveryEndpoints();
app.MapNotificationEndpoints();
if (app.Environment.IsDevelopment())
    app.MapGet("/api/v1/development/info", () => Results.Ok(new
    {
        environment = "Development",
        simulatedOtpEnabled = builder.Configuration["Authentication:SmsProvider"] == "Development" &&
            builder.Configuration.GetValue<bool>("Authentication:ExposeDevelopmentCodes")
    }));

// Liveness only: this endpoint does not probe the database.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (builder.Configuration["migrate"] == "true")
{
    await using var scope = app.Services.CreateAsyncScope();
    await DatabaseMaintenance.MigrateAsync(scope.ServiceProvider, CancellationToken.None);
    Console.WriteLine("All module migrations applied."); return;
}
if (args.Contains("--grant-staff-permissions") || args.Contains("--revoke-staff-permissions"))
{
    var grant = builder.Configuration["grant-staff-permissions"];
    var revoke = builder.Configuration["revoke-staff-permissions"];
    if (grant is not null && revoke is not null || !Guid.TryParse(grant ?? revoke, out var staffId))
        throw new InvalidOperationException("Choose one staff provisioning command with a verified user GUID.");
    string[] permissions = grant is null ? [] : (builder.Configuration["permissions"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (grant is not null && permissions.Length == 0) throw new InvalidOperationException("Specify supported --permissions separated by commas.");
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<StaffAccessService>().SetAsync(staffId, permissions, CancellationToken.None);
    Console.WriteLine("Staff permissions updated; sign in again before exchanging for a staff session."); return;
}

var catalogOperator = builder.Configuration["issue-development-catalog-token"];
var inventoryOperator = builder.Configuration["issue-development-inventory-token"];
var ordersOperator = builder.Configuration["issue-development-orders-token"];
var deliveryOperator = builder.Configuration["issue-development-delivery-token"];
var paymentsOperator = builder.Configuration["issue-development-payments-token"];
var operationsOperator = builder.Configuration["issue-development-operations-token"];
if (new[] { catalogOperator, inventoryOperator, ordersOperator, deliveryOperator, paymentsOperator, operationsOperator }.Count(x => x is not null) > 1)
    throw new InvalidOperationException("Choose one development management token command.");
if ((catalogOperator ?? inventoryOperator ?? ordersOperator ?? deliveryOperator ?? paymentsOperator ?? operationsOperator) is { } userIdText)
{
    if (!app.Environment.IsDevelopment() || !Guid.TryParse(userIdText, out var userId))
        throw new InvalidOperationException("This local Development command requires a verified user's GUID.");
    await using var scope = app.Services.CreateAsyncScope();
    string[] permissions = operationsOperator is not null
        ? [CatalogPolicies.Permission, InventoryPolicies.Permission, OrderPolicies.Permission, DeliveryPolicies.Permission, PaymentPolicies.Permission]
        : [catalogOperator is not null ? CatalogPolicies.Permission : inventoryOperator is not null ? InventoryPolicies.Permission :
            ordersOperator is not null ? OrderPolicies.Permission : deliveryOperator is not null ? DeliveryPolicies.Permission : PaymentPolicies.Permission];
    var result = await scope.ServiceProvider.GetRequiredService<DevelopmentStaffAccess>().IssueAsync(userId, permissions, CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    return;
}

app.Run();

public partial class Program { }
