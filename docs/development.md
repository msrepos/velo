# Development

## Requirements and API

Install the .NET SDK selected by `global.json`. For Visual Studio, enable the
ASP.NET and web development workload and use a version supporting .NET 10.

Open `Velo.slnx`, set `Velo.Api` as startup project and use the `http` profile.
From the repository root:

```powershell
dotnet restore Velo.slnx
dotnet tool restore
dotnet build Velo.slnx --no-restore
dotnet run --project src/Velo.Api --launch-profile http
Invoke-WebRequest http://localhost:5080/health/live
```

Run the HTTP request in a separate terminal, or use `src/Velo.Api/Velo.Api.http`.
The health endpoint checks process liveness only. It does not check SQL Server.
Customer auth routes are available under `/api/v1/auth`. Catalog browsing uses
`/api/v1/catalog`; permission-protected management uses `/api/v1/admin/catalog`.
Inventory management uses `/api/v1/admin/inventory`; see [Inventory API](inventory-api.md).
Customer addresses use `/api/v1/customers/me/addresses`; the customer cart uses
`/api/v1/cart`. See [addresses and cart](customer-cart-api.md) for versions and Postman.
Swagger endpoints have not been implemented yet.

## Local SQL Server and connection configuration

Use an existing SQL Server instance, or start the development container with
Docker Desktop running Linux containers:

```powershell
Copy-Item infrastructure/docker/.env.example infrastructure/docker/.env
# Edit .env and set a strong local MSSQL_SA_PASSWORD first.
docker compose --env-file infrastructure/docker/.env -f infrastructure/docker/compose.yml up -d
docker compose --env-file infrastructure/docker/.env -f infrastructure/docker/compose.yml ps
```

The instance is available at `localhost,1433`. The Developer edition is for
development/testing only. The compose file creates the SQL Server instance;
the `velo` database and its Identity schema are created by EF Core migrations.
Stop it while retaining data with the same compose command followed by `stop`.

Configure `ConnectionStrings:Velo` and `Authentication:OtpHashKey` through Manage
User Secrets on Velo.Api before running the API. See [local auth setup](authentication-api.md).
If Visual Studio cannot access those User Secrets, an ignored `appsettings.Local.json`
can supply these two missing values in Development only. Existing configured
values retain priority. This local file is copied for local debugging but is
explicitly excluded from publishing; never share or commit it.
See [database migrations](architecture/database-migrations.md) for setup,
CLI commands and Visual Studio Package Manager Console commands.

The HTTP profile is intended for local development. Configure TLS, allowed hosts,
authentication and deployment settings before exposing the service externally.

## Next implementation

Customer authentication, Catalog, Inventory, saved addresses and carts are implemented.
Apply migrations for IdentityDbContext, CatalogDbContext, InventoryDbContext,
CustomersDbContext and OrdersDbContext; see
[Catalog setup](catalog-api.md) and [Inventory setup and management tokens](inventory-api.md).
See [Customers/Orders migration commands](customer-cart-api.md).
COD checkout, flat delivery fees, radius-based warehouse selection, confirmation,
cancellation and expiry are implemented. See [Orders API](orders-api.md) for
configuration, staff tokens and the two additional Inventory/Orders migrations.
Development uses 20 EGP / 5 km / 10 minutes; checkout is disabled by default
outside Development until explicitly configured.
Real SMS delivery is deferred by user choice.
Fulfillment, delivery/receipt proof, COD collection, cash handover, in-app notifications
and server-provisioned staff sessions are now implemented. See [full cycle](fulfillment-api.md)
and [deployment](deployment.md). Real SMS/push/card integrations need provider setup.
