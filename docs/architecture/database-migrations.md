# SQL Server and Identity migrations

## Layout

One SQL Server database, `velo`, is shared by the application. Each module owns
its schema, DbContext and migrations. The first implementation is Identity:

Catalog now has `CatalogDbContext`, a `catalog` schema and independent migrations
and history. See [Catalog API](../catalog-api.md) for its setup commands. Apply
all contexts' migrations explicitly; running Identity migrations alone does not
update Catalog or Inventory. Inventory uses `InventoryDbContext`, the `inventory`
schema and its own migration history. See [Inventory API](../inventory-api.md)
for CLI and Package Manager Console commands.
Customers and Orders also have their own contexts and histories for addresses
and carts. Apply both explicitly using the commands in
[Customer addresses and cart](../customer-cart-api.md).
Checkout adds `OrderReservationOwnership` in Inventory and `CheckoutOrders` in
Orders. Apply both contexts before running this API version; see [Orders setup](../orders-api.md).
Fulfillment adds Identity.StaffSessions, Orders.FulfillmentAndPayments and the
initial Delivery/Payments/Notifications migrations. Apply all eight contexts with
the explicit `--migrate true` host command. New COD records for pre-Payments orders
are established lazily when their order is read/processed. See [full cycle](../fulfillment-api.md).

```text
src/Modules/Identity/Velo.Modules.Identity/Infrastructure/
  IdentityModuleExtensions.cs
  Persistence/
    IdentityDbContext.cs
    Models/ApplicationUser.cs
    Migrations/
      <timestamp>_InitialIdentity.cs
      <timestamp>_InitialIdentity.Designer.cs
      IdentityDbContextModelSnapshot.cs
```

The migration creates seven tables under `identity`: Users, Roles, UserRoles,
UserClaims, RoleClaims, UserLogins and UserTokens. Its history is stored in
`identity.__EFMigrationsHistory`, separate from future modules.

Users and roles use GUID identifiers. PhoneNumber is required, limited to 16
characters and uniquely indexed. The sign-in application must normalize phone
numbers to E.164 before storage; the database does not normalize them. Email
and PasswordHash remain optional for the planned passwordless customer flow.
Identity's UserTokens table is separate from the implemented `Sessions` and
`OtpChallenges` tables. `CustomerOtpAndSessions` adds those tables, `AuthThrottles`,
`DataProtectionKeys`, an active-user flag and the Customer role.
`RegistrationProfile` adds nullable `Users.Name` and pending registration name/email
columns on `OtpChallenges`; existing accounts remain unchanged. Identity's existing
Email/NormalizedEmail columns store the email after successful phone verification.

Identity stores, customer auth endpoints and protected token issuance are registered
by the host. Only the Customer role is seeded; no users or administrative roles
are seeded. SMS delivery is simulated locally. Migrations run
explicitly, not automatically on API startup. Do not use EnsureCreated alongside
the migration history.

## Local connection

The API uses `ConnectionStrings:Velo`. Store it using Visual Studio's **Manage
User Secrets** on Velo.Api, or through the CLI. The project's secret ID is
`velo-api-development`. Never include a real password in source files.

Example secrets structure (replace placeholders locally):

```json
{
  "ConnectionStrings": {
    "Velo": "Server=<server>;Database=velo;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;Persist Security Info=False"
  }
}
```

TrustServerCertificate is for the local development SQL Server. User Secrets
are a development facility, not encrypted production secret storage. In deployed
environments provide `ConnectionStrings__Velo` through the deployment secret store.

## CLI

Run from the solution root in PowerShell. The API startup project supplies the
configuration and the module project contains the migrations.

```powershell
dotnet tool restore
dotnet restore Velo.slnx
$env:ASPNETCORE_ENVIRONMENT = 'Development'

# Create/update the configured database.
dotnet ef database update --context IdentityDbContext --project src/Modules/Identity/Velo.Modules.Identity --startup-project src/Velo.Api

# After an intentional model change, add a new migration; retain applied files.
dotnet ef migrations add <ChangeName> --context IdentityDbContext --project src/Modules/Identity/Velo.Modules.Identity --startup-project src/Velo.Api --output-dir Infrastructure/Persistence/Migrations

# Generate a script for review against an existing target database.
dotnet ef migrations script --idempotent --context IdentityDbContext --project src/Modules/Identity/Velo.Modules.Identity --startup-project src/Velo.Api --output infrastructure/database/identity.sql

dotnet ef migrations has-pending-model-changes --context IdentityDbContext --project src/Modules/Identity/Velo.Modules.Identity --startup-project src/Velo.Api
```

The generated SQL script applies schema changes to the selected database; it
does not create the database itself. `database update` creates it if necessary
and the configured login has permission.

## Visual Studio Package Manager Console

Set Velo.Api as the startup project. Use:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Update-Database -Context IdentityDbContext -Project Velo.Modules.Identity -StartupProject Velo.Api
Add-Migration <ChangeName> -Context IdentityDbContext -Project Velo.Modules.Identity -StartupProject Velo.Api -OutputDir Infrastructure/Persistence/Migrations
```

## Verification

`infrastructure/database/verify-identity.sql` checks the expected tables, phone
constraint and migration history without writing data. Run it in SSMS against
the intended `velo` database. Repeating `database update` must report that the
database is already up to date. `has-pending-model-changes` must report no changes
after generating the migration.
