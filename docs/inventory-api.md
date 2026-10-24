# Inventory API

Inventory owns the `inventory` schema and `InventoryDbContext` in the shared
SQL Server database. It references `Velo.Modules.Catalog.Contracts` to look up
variants, without querying Catalog tables directly. Stock is tracked per
warehouse and **variantId**, not productId. Quantities are integer SKU units.

## Local setup

Run from the solution root, with the API stopped while building:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --context InventoryDbContext --project src/Modules/Inventory/Velo.Modules.Inventory --startup-project src/Velo.Api
```

In Visual Studio Package Manager Console:

```powershell
Update-Database -Context InventoryDbContext -Project Velo.Modules.Inventory -StartupProject Velo.Api
```

Identity and Catalog migrations must also be applied. Inventory has its own
`inventory.__EFMigrationsHistory`. The reviewed idempotent script is
`infrastructure/database/inventory.sql`; `verify-inventory.sql` is read-only.
No warehouses, balances or users are seeded by this migration.

All routes require an access token with the `inventory.manage` permission.
Customer OTP tokens and Catalog-only tokens return 403. Missing/expired tokens
return 401. For local Development only, use a verified, active user's actual
GUID from `/api/v1/auth/me`, replacing `<USER_GUID>` below:

```powershell
dotnet run --project src/Velo.Api --no-build --launch-profile http -- --issue-development-inventory-token <USER_GUID>
```

This command exits after issuing a 15-minute access-only token. Start the API
separately using Visual Studio's `http` profile. This development helper is not
a production staff login flow; staff provisioning is still to be implemented.
Keep the token local and set `Authorization: Bearer <accessToken>`.

## Routes

All paths below are relative to `/api/v1/admin/inventory`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET / POST | `/warehouses` | List / create warehouses |
| GET / PUT | `/warehouses/{id}` | Read / update warehouse |
| GET | `/warehouses/{id}/balances` | List stored SKU balances |
| GET | `/warehouses/{id}/balances/{variantId}` | Read balance; zero for known SKU without stock |
| GET | `/warehouses/{id}/movements` | Ledger; optional `variantId` filter |
| POST | `/warehouses/{id}/adjustments` | Receive stock or record a signed adjustment |
| POST | `/transfers` | Transfer available units between warehouses |
| POST | `/reservations` | Reserve one or more SKUs atomically |
| GET | `/reservations/{id}` | Current reservation status |
| POST | `/reservations/{id}/commit` | Consume reserved units |
| POST | `/reservations/{id}/release` | Return reserved units to availability |

Lists accept `page` (default 1) and `pageSize` (default 20, maximum 100), and
return `items`, `page`, `pageSize`, `totalCount`.

Create warehouse (201):

```json
{
  "code": "CAIRO-01",
  "name": "Cairo warehouse",
  "type": "Fixed",
  "address": "Nasr City",
  "latitude": 30.050000,
  "longitude": 31.350000,
  "isActive": true
}
```

`code` is unique, normalized to uppercase, 1–32 ASCII letters/digits/hyphens/
underscores. `type` is `Fixed` or `Mobile`. Coordinates are optional but must
be supplied together, within valid ranges and with at most six decimal places.
For PUT, send the complete fields and the latest response's base64 `version`.
A stale version returns 409. Disable a warehouse with `isActive: false`;
new stock changes/reservations are blocked, but existing reservations can finish.
Mobile coordinates are manually stored locations, not live vehicle tracking.

Adjustment (200):

```json
{
  "operationId": "<new GUID>",
  "variantId": "<Catalog variant GUID>",
  "quantityDelta": 20,
  "reason": "Initial receipt"
}
```

Use a negative delta for a correction. Delta must be nonzero with absolute value
at most 1,000,000. `reason` is required, maximum 500 characters. A balance exposes
`onHand`, `reserved`, `available = onHand - reserved`. Adjustments cannot make
on-hand less than reserved. Known inactive SKUs can still be adjusted/transferred.

Transfer (200):

```json
{
  "operationId": "<new GUID>",
  "sourceWarehouseId": "<source GUID>",
  "targetWarehouseId": "<target GUID>",
  "variantId": "<Catalog variant GUID>",
  "quantity": 3,
  "reason": "Replenishment"
}
```

Both warehouses must be active and different. The transfer moves available
units atomically and creates two ledger entries. This represents an immediate
stock transfer; dispatch/receipt and in-transit stock workflows are future work.

Reservation (200):

```json
{
  "operationId": "<new GUID>",
  "warehouseId": "<warehouse GUID>",
  "reference": "checkout-unique-reference",
  "items": [{ "variantId": "<Catalog variant GUID>", "quantity": 5 }],
  "lifetimeMinutes": 10
}
```

Supply 1–100 distinct SKUs with quantities 1–1,000,000. Their variant, product
and category must all be active. Reference is globally unique, maximum 64
characters. Lifetime defaults to 10 minutes and allows 1–30. All items succeed
or roll back together. The returned reservation `id` equals its `operationId`.
Commit/release require no body.

| State | Effect |
| --- | --- |
| Pending | Reserved increases; on-hand stays unchanged |
| Committed | Reserved and on-hand decrease by the reservation quantity |
| Released | Reserved decreases; on-hand stays unchanged |
| Expired | Same stock effect as release, performed automatically |

Repeated commit of a committed reservation or release of a released/expired
reservation succeeds without another movement. Other terminal transitions
return 409. Expired reservations cannot be committed.

The expiry worker runs every 30 seconds, processing up to 100 due reservations
per pass. Stock operations, balance reads and reservation reads also expire due
reservations on demand. Database transaction locks prevent concurrent workers
or HTTP requests from releasing/consuming the same units twice. Requests that
cross the expiry deadline while waiting for a lock can return `reservation_expired`;
the next expiry pass releases the stock. `Inventory:EnableExpiryWorker` defaults
to true; tests disable it and exercise expiry explicitly with a controlled clock.

## Retries, audit and errors

Adjustments, transfers and reservation creation require a new `operationId`
for each logical command. Retry with the **same ID, same payload and same user**
after a timeout. A successful command's original receipt is stored in the same
transaction as its stock changes and returned on replay. Changed payload/user
returns 409 `idempotency_conflict`. Reservation item order does not affect replay.
Failed commands roll back and do not consume the operation ID. A replay returns
the original receipt, so use GET for the current balance/reservation status.

Each ledger entry includes operation ID, actor user ID, deltas, resulting
quantities, reason/reference and timestamp. Automatic expiry has a null actor.
SQL constraints enforce `0 <= reserved <= onHand`; sorted transaction-scoped
SQL application locks serialize conflicting stock changes across API instances.
Do not update balance or ledger tables manually.

Errors use Problem Details with a `code`: validation 400, absent warehouse or
reservation 404, stock shortage/inactive warehouse/invalid state/stale version/
duplicate code or reference 409. Unknown variants cannot receive stock.

These are management APIs. [Orders checkout](orders-api.md) now uses
`IOrderStock` for warehouse selection and owned reservations. Direct management
commit/release of an Orders reservation returns `order_managed_reservation`;
use the Order confirmation/cancellation flow. Automatic expiry remains active.

## Postman

Import `tools/postman/Velo.Inventory.postman_collection.json`. Set `baseUrl`,
`staffAccessToken` to the Inventory token, and `variantId` to an **active variant**
created in Catalog. Run requests in order within the reservation lifetime.
The collection creates two uniquely named warehouses and real demo stock in
your local database. It demonstrates adjustment replay, release, commit and
transfer. It finishes with source on-hand 13 and target on-hand 3, both reserved 0.
Rerunning the complete collection creates a fresh pair of warehouses.
