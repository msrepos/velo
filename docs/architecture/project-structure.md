# Project structure

```text
Velo.slnx
src/
  Velo.Api/
  Modules/
    Identity/Velo.Modules.Identity/
    Identity/Velo.Modules.Identity.Contracts/
    Customers/Velo.Modules.Customers/
    Customers/Velo.Modules.Customers.Contracts/
    Catalog/Velo.Modules.Catalog/
    Catalog/Velo.Modules.Catalog.Contracts/
    Inventory/Velo.Modules.Inventory/
    Inventory/Velo.Modules.Inventory.Contracts/
    Orders/Velo.Modules.Orders/
    Payments/Velo.Modules.Payments/
    Payments/Velo.Modules.Payments.Contracts/
    Delivery/Velo.Modules.Delivery/
    Delivery/Velo.Modules.Delivery.Contracts/
    Notifications/Velo.Modules.Notifications/
    Notifications/Velo.Modules.Notifications.Contracts/
tests/
docs/
infrastructure/docker/
```

Each module is a class library containing `Domain`, `Application`,
`Infrastructure` and `Endpoints`. Folder entries in the project files make empty
folders visible in Visual Studio; `.gitkeep` files preserve directories in Git.

| Module | Responsibility |
| --- | --- |
| Identity | ASP.NET Core Identity, phone/OTP authentication, sessions and access permissions |
| Customers | Customer profiles and delivery addresses |
| Catalog | Products, categories and SKUs |
| Inventory | Warehouses, stock, reservations and movements |
| Orders | Cart, checkout, totals and order lifecycle |
| Payments | Providers, webhooks, refunds and reconciliation |
| Delivery | Drivers, assignment, delivery lifecycle and tracking |
| Notifications | Push, SMS and email integrations |

`Velo.Api` is the only executable and references all eight modules. They run in
one deployment. Inventory references the small Catalog.Contracts project to
look up variant existence/publication through `ICatalogVariants`; Catalog supplies
the implementation. Inventory does not reference Catalog's implementation assembly.
Orders also references Catalog.Contracts, using `ICatalogOffers` for batch lookup
of current names/prices/publication. Customer addresses do not access Identity tables.
Orders uses Customers.Contracts to snapshot an owned address, and Inventory.Contracts
for fulfillment/reservation operations. The API composition root enlists the four
module contexts in one physical SQL transaction for order workflows. Modules keep
their schemas/contracts; extracting them requires a durable distributed workflow.

- Domain: entities, value objects, invariants and domain events.
- Application: use cases, validation, DTOs and required interfaces.
- Infrastructure: persistence mappings and external adapters.
- Endpoints: HTTP routes invoking application operations.

Domain code must not depend on HTTP or persistence libraries. These are folder
conventions inside each assembly, not compiler-enforced layer boundaries.
All eight modules own separate schemas/DbContexts/migration histories
within the same SQL Server database. Access other
modules through explicit contracts rather than their tables.
The order coordinator now enlists seven business contexts, including Delivery,
Payments and Notifications; Identity remains the separate authentication boundary.

See [the authentication decision](authentication.md) for the accepted customer
phone/OTP approach and the boundary between Identity and Notifications.

`Directory.Build.props` centralizes framework/compiler defaults, and `global.json`
selects the SDK. Tests, documentation, infrastructure and root configuration are
visible as solution folders. There are no legacy Go or PostgreSQL components.
