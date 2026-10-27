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
all eight DbContexts point to its unique `velo_catalog_tests_<guid>` database before
applying migrations. Run all test projects with `dotnet test Velo.slnx`.

`Velo.Inventory.IntegrationTests` uses a unique `velo_inventory_tests_<guid>`
database, validates all eight contexts before migrating, disables the expiry
worker, and uses a controlled clock for expiry. It covers permission isolation,
warehouse validation/rowversions, durable command replay, concurrent last-unit
reservations and receipt retries, atomic multi-item rollback and transfers,
reserved-stock protection, terminal state transitions and concurrent expiry.
Tests drop only their own generated databases. No demo stock is seeded into `velo`.

`Velo.Commerce.IntegrationTests` creates a unique `velo_commerce_tests_<guid>`
database and replaces/validates all eight contexts. It covers customer ownership,
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
all eight contexts before migrating. It disables both expiry workers, controls time,
and verifies checkout replay, customer ownership, preview/expected totals, staff
permissions, nearest eligible warehouse, atomic multi-item availability, racing
last-unit checkouts, confirmation/cancellation races and repeated expiry. Failures
are injected after reserving, consuming stock, and saving the order/cart to prove
cross-module transaction rollback and safe retries. Every test uses local SQL Server.

Fulfillment tests cover courier ownership/capacity, receipt-code attempts and
rotation, private/stale location, cash collection and settlement replay, explicit
restock decisions, notifications and rollback across order/shipment/payment/inbox.
Staff tests prove server-provisioned exchange, permission-preserving refresh,
revocation and recent-OTP requirements. Gateway tests use a fake HTTP handler:
no real SMS is sent. Readiness tests verify liveness remains healthy while a
temporary test-only migration history entry is absent and restore it in finally.
The full collection is independently exercised by `tools/postman/Verify-FullCycle.ps1`
using its own `velo_postman_tests_<guid>` database and real local HTTP requests.
