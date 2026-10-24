# Test plan

`Velo.Auth.IntegrationTests` covers the customer phone/OTP HTTP flow with a real
SQL Server database. It creates and drops a unique `velo_auth_tests_<guid>`
database, and asserts the actual DbContext database name before applying migrations.
Use `dotnet test tests/Velo.Auth.IntegrationTests`. Connection credentials come from
`VELO_TEST_SQL_CONNECTION` or the Velo.Api User Secrets; never commit them.

Covered: normalization, expiry, attempt limits, quotas, resend invalidation,
concurrent verification/refresh, refresh replay, logout, ownership, disabled
accounts, role isolation, delivery failure and development safety checks.

Add meaningful tests alongside implemented features:

`Velo.Catalog.IntegrationTests` covers public visibility, management permissions,
price/SKU validation, uniqueness, paging and SQL rowversion conflicts. It verifies
all five DbContexts point to its unique `velo_catalog_tests_<guid>` database before
applying migrations. Run all test projects with `dotnet test Velo.slnx`.

`Velo.Inventory.IntegrationTests` uses a unique `velo_inventory_tests_<guid>`
database, validates all five contexts before migrating, disables the expiry
worker, and uses a controlled clock for expiry. It covers permission isolation,
warehouse validation/rowversions, durable command replay, concurrent last-unit
reservations and receipt retries, atomic multi-item rollback and transfers,
reserved-stock protection, terminal state transitions and concurrent expiry.
Tests drop only their own generated databases. No demo stock is seeded into `velo`.

`Velo.Commerce.IntegrationTests` creates a unique `velo_commerce_tests_<guid>`
database and replaces/validates all five contexts. It covers customer ownership,
single default addresses under concurrency, address versions/limits/validation,
current server prices, unpublished cart items, required/stale cart ETags, concurrent
device edits, clearing and malformed JSON. Cart tests verify no Inventory reservation
or balance is created. Use `dotnet test tests/Velo.Commerce.IntegrationTests`.

- Module unit tests for business rules and invalid state transitions.
- Integration tests for API workflows and SQL Server persistence.
- Architecture tests for module references and dependency boundaries.

Prioritize concurrent stock reservations, order/payment idempotency, webhook
retries and order cancellation compensation. Integration tests must use a
dedicated test database, never development or production data.

`Velo.Orders.IntegrationTests` creates/drops `velo_order_tests_<guid>` and validates
all five contexts before migrating. It disables both expiry workers, controls time,
and verifies checkout replay, customer ownership, preview/expected totals, staff
permissions, nearest eligible warehouse, atomic multi-item availability, racing
last-unit checkouts, confirmation/cancellation races and repeated expiry. Failures
are injected after reserving, consuming stock, and saving the order/cart to prove
cross-module transaction rollback and safe retries. Every test uses local SQL Server.
