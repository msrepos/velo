# Checkout and orders

The first order flow supports customer checkout, order history, customer
cancellation before confirmation, and staff confirmation. Payment is **CashOnDelivery**;
orders remain **Unpaid**, including after confirmation. Payment collection,
packing, dispatch, delivery, returns and cancellation after confirmation are
later milestones. Confirmation consumes stock; it does not mark an order delivered.

## Configuration

`Orders:EnableCheckout` defaults to false outside the Development settings. Configure
these settings explicitly before enabling checkout in another environment:

```json
{
  "Orders": {
    "EnableCheckout": true,
    "DeliveryFee": 20,
    "DeliveryRadiusKm": 5,
    "ReservationMinutes": 10,
    "EnableExpiryWorker": true
  }
}
```

The example is the local Development configuration: **20 EGP flat delivery fee,
5 km radius and 10 minutes to confirm**. These are demo business settings, not
production rates. Fee allows 0–1000 EGP with at most two decimal places, radius
must be finite and greater than 0 up to 50 km, and reservation lifetime 1–30 minutes.
Invalid enabled configuration fails startup. Disabled checkout returns 503 for
new orders; existing orders can still be read/confirmed/cancelled/expired and
successful checkout IDs can still be replayed.

Fulfillment selects the nearest active warehouse with coordinates, inside the
configured straight-line radius, with enough **available** units for every item.
Fixed and Mobile warehouses are eligible; stored coordinates are not live tracking.
There is no split fulfillment or road/travel-time calculation. An inactive,
unlocated, out-of-range or insufficient warehouse cannot fulfill the order. If the
nearest warehouse lacks stock, the next eligible warehouse is considered. The
actual reservation rechecks stock atomically; a racing request can still get 409
and safely retry after refreshing. Dedicated delivery zones/rates are future work.

## Customer endpoints

Use the ordinary OTP customer bearer token. All responses are private/no-store.
Customer ID comes from the authenticated session. Other customers' orders return
404 and never appear in lists.

| Method | Path | Behavior |
| --- | --- | --- |
| POST | `/api/v1/orders/checkout/preview` | Show current fees/total and eligible warehouse; no reservation |
| POST | `/api/v1/orders/checkout` | Create order and reserve stock, 201; replay 200 |
| GET | `/api/v1/orders` | Own orders, `page=1&pageSize=20` (maximum 100) |
| GET | `/api/v1/orders/{id}` | Own order with snapshots and status history |
| POST | `/api/v1/orders/{id}/cancel` | Cancel Pending order and release stock |

First create a saved address and prepare the cart. POST `/api/v1/orders/checkout/preview`
with `addressId` and `addressVersion`. It returns `cart` (including its `version`),
the address snapshot, an eligible `warehouseId`, `deliveryFee`, final `total`
and `reservationMinutes`. Display the fee/total to the customer before placing
the order. Preview does not create an order, clear the cart or reserve units;
it may release already-expired reservations while checking availability.
Use the preview's quoted cart version as **If-Match** and `total` as
**expectedTotal** when the customer agrees. Use a published Catalog variant ID
in cart items. Send:

```http
POST /api/v1/orders/checkout
Authorization: Bearer <customer token>
Content-Type: application/json
If-Match: "<latest cart version>"

{
  "operationId": "<new GUID for this checkout>",
  "addressId": "<own address GUID>",
  "addressVersion": "<latest address version, base64>",
  "expectedTotal": 40.00,
  "paymentMethod": "CashOnDelivery"
}
```

The server verifies address ownership/version, the complete current cart ETag
(including current offers), publication and stock. It freezes recipient/address
details, item names/SKUs/unit prices/quantities/totals, delivery fee and currency.
The mobile client cannot choose the customer, warehouse, prices, fee or total.
`expectedTotal` is required and is only checked against the server calculation;
it never overrides it. A changed fee/total returns 409 `checkout_total_changed`
before any reservation/order/cart changes. Refresh the preview and get the
customer's agreement before trying again. Preview does not lock stock/prices
or guarantee that the final warehouse will be the same.
Order totals are capped at 1,000,000 EGP. Checkout clears the cart only after
success; failure preserves the cart and rolls back stock and order changes.

Response fields include `id`, `warehouseId`, `reservationId`, `status`, `currency`,
`paymentMethod`, `paymentStatus`, `subtotal`, `deliveryFee`, `total`, `address`,
`createdAt`, `expiresAt`, `items` and `history`. The order ID is the operationId.
Names, prices and the delivery address remain as ordered even if Catalog changes
or the saved address is later changed/deleted. The copied address version is an
audit value, not a link to a live address. Order histories retain all transitions.
The list returns `items`, `page`, `pageSize`, `totalCount`.

For cancellation, send `{"reason":"Changed my mind"}` (1–500 trimmed characters).
Repeated cancellation of a Cancelled order returns its original state/reason
without additional movements. Cancelling an already Expired order returns Expired.
Confirmed orders cannot be cancelled through this endpoint (409); their stock
has already been consumed and needs a future return/refund workflow. Cancellation
and expiry do not put items back in the cart.

## Confirmation and lifecycle

`POST /api/v1/admin/orders/{id}/confirm` requires `orders.manage`; no body is
needed. Customer, Catalog-only and Inventory-only tokens return 403.
Staff can also GET `/api/v1/admin/orders` (optional `status` filter and the same
paging) and `/api/v1/admin/orders/{id}` to view the queue/details. Filters accept
Pending, Confirmed, Cancelled or Expired. Pending excludes elapsed deadlines,
and Expired includes due Pending orders, reconciling their state when read.

| State | Stock effect | Allowed next state |
| --- | --- | --- |
| Pending | On-hand unchanged, reserved increases | Confirmed / Cancelled / Expired |
| Confirmed | On-hand and reserved decrease | Fulfillment workflow is future work |
| Cancelled | Reserved released, on-hand unchanged | Terminal in this milestone |
| Expired | Reserved released, on-hand unchanged | Terminal |

Repeated confirmation succeeds without another stock deduction. Confirmation of
Cancelled/Expired returns 409. Concurrent cancel/confirm serializes so only one
transition wins. History stores the actor internally, and automatic expiry has
no order-history actor. Customer responses show status, reason and time.

Pending orders must be confirmed within `expiresAt`. Order and Inventory expiry
workers run every 30 seconds, with batches up to 100 due records per pass; expiry
can take multiple passes for a backlog. Order reads, replay, cancellation and
confirmation also reconcile due expiry. An expired order cannot be confirmed
even when workers have not run yet. Requests crossing the deadline while waiting
for inventory locks may return `reservation_expired`; a later read/worker reconciles it.

Orders owns its reservations. Inventory management endpoints reject direct
commit/release of an Orders reservation with 409 `order_managed_reservation`.
Automatic Inventory expiry can still release it, and Orders then reconciles the
status. Other Inventory reservations keep their existing management behavior.

## Retries and atomicity

After a timeout, resend **the original operationId, JSON body and If-Match header**.
A successful operation is found before validating the now-empty cart or a changed/
deleted address and returns the same order with its current status (200). It does
not create another reservation or order. Reusing the ID with a different customer,
address, address version, expected total, payment method or cart header returns 409
`checkout_idempotency_conflict`. Keep the original request until its outcome is known.
Failed transactions do not consume the ID. A genuinely new checkout needs a new GUID.

The composition root coordinates Orders, Inventory, Customers and Catalog with
**one physical SQL connection and transaction**. It serializes the customer's
cart with the same database application lock as cart edits, plus the operation ID.
Inventory keeps its stock locks and constraints. Order insert, reservation,
ledger and cart clearing commit/roll back together; confirmation and cancellation
also update order and stock atomically. No distributed SQL transaction is needed.
Tests inject failures after reservation, stock deduction and order/cart saves.

Modules communicate through Customers.Contracts / Inventory.Contracts /
Catalog.Contracts. The composition-root SQL adapter knows their contexts solely
to enlist them. This implementation requires one SQL Server database and rejects
different module databases. Extracting services later requires replacing this
coordinator with durable messaging/saga/outbox workflows; separate databases are
not a configuration-only change.

Common errors: 400 invalid checkout/payment/reason, 401 unauthenticated, 403 missing
management permission, 404 own address/order absent, 409 stale address/empty cart/
unavailable variant/no eligible warehouse/invalid state/idempotency conflict,
412 stale cart ETag, 428 missing cart ETag, 503 checkout disabled. Errors use
Problem Details with a machine-readable `code`.

## Development staff token and migrations

Use a verified active user's actual GUID from `/api/v1/auth/me`. Replace the
placeholder locally, then run from the solution root:

```powershell
dotnet run --project src/Velo.Api --no-build --launch-profile http -- --issue-development-orders-token <actual-user-GUID>
```

The command exits after issuing an access-only token valid for 15 minutes. It is
Development-only and is not a production staff sign-in/provisioning implementation.
For Postman warehouse setup, also issue a separate Inventory token using
`--issue-development-inventory-token`. Normal OTP login supplies the customer token.

Apply **Inventory and Orders** migrations before running this API version:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --context InventoryDbContext --project src/Modules/Inventory/Velo.Modules.Inventory --startup-project src/Velo.Api
dotnet ef database update --context OrdersDbContext --project src/Modules/Orders/Velo.Modules.Orders --startup-project src/Velo.Api
```

Visual Studio Package Manager Console:

```powershell
Update-Database -Context InventoryDbContext -Project Velo.Modules.Inventory -StartupProject Velo.Api
Update-Database -Context OrdersDbContext -Project Velo.Modules.Orders -StartupProject Velo.Api
```

`OrderReservationOwnership` adds a defaulted OwnerModule column without changing
existing reservation ownership. `CheckoutOrders` adds Orders, OrderLines and
OrderEvents under `orders`. InitialCart is retained. Scripts `inventory.sql` and
`orders.sql` are idempotent; `verify-orders.sql` performs read-only checks. No demo
customers/orders/warehouses are seeded by migrations.

## Postman

Import `tools/postman/Velo.Orders.postman_collection.json`. Set `accessToken`
(customer), `ordersStaffAccessToken`, `inventoryStaffAccessToken` and an active
Catalog `variantId`. Use No environment, start Velo.Api with `http`, and run in
order within 10 minutes. See the collection description for expected status codes.
The collection creates a demo warehouse/receipt and address, **clears the customer's
existing cart**, confirms one order, cancels another, tests retries, then deletes
its demo address while demonstrating that the order's snapshot survives.
Use a test customer. It leaves real local order history and inventory movements.
Do not export populated tokens.
