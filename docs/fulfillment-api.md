# Full local delivery and cash-on-delivery cycle

Import **tools/postman/Velo.FullCycle.postman_collection.json**. This is the
complete local demonstration: **57 requests**, automatic customer/driver OTP,
demo Catalog/warehouse/stock, checkout, preparation, assignment, location,
customer receipt code, delivery, cash reconciliation, inbox and failed return.
It creates its own customers/cart; existing customers' carts are not cleared.

## Run in Postman

1. Use an existing active verified user's real GUID from `/api/v1/auth/me`.
2. From the solution root issue a fresh local operations token:

   ```powershell
   dotnet run --project src/Velo.Api --no-build --launch-profile http -- --issue-development-operations-token REAL_VERIFIED_USER_GUID
   ```

   Replace REAL_VERIFIED_USER_GUID with the actual `id`, not that literal text.
   Copy only `accessToken` from the JSON output. This access-only development
   token has all five management permissions and expires in 15 minutes.
3. Run Velo.Api in Visual Studio with the `http` profile.
4. Import the FullCycle JSON. Select **No environment**. Set collection variable
   `operationsAccessToken`; `baseUrl` defaults to `http://localhost:5080`.
   No variantId or driver account is required: the collection creates them.
5. Run **01–57 in order** with Collection Runner, within ten minutes. The first
   request requires local Development with simulated OTP before generating any
   demo accounts. The incorrect-code request expects 400, and reading the receipt
   code after delivery expects 409; these are successful negative checks.

The first order delivers two units, becomes Delivered/Paid, and its exact cash
is handed over to operations. The second order consumes one unit, fails delivery,
and is returned with `restock: false` to demonstrate unusable/perishable goods.
Final warehouse on-hand is **17**, reserved **0**, driver outstanding cash **0**.
Demo users, orders, receipts and movements remain for inspection. Do not export
populated tokens/codes. The collection never calls a real SMS provider.

## Fulfillment and dispatch

All `/api/v1/admin/orders` operations require `orders.manage`:

| POST action under `/{orderId}` | Input | Transition |
| --- | --- | --- |
| `/confirm` | No body | Pending → Confirmed; consumes reservation |
| `/prepare` | No body | Confirmed → Preparing |
| `/ready` | No body | Preparing → ReadyForDispatch |
| `/assign` | `driverId` | Assign/reassign before departure or retry failed delivery |
| `/cancel` | `reason`, optional `restockWarehouseId` | Cancel before departure; restore consumed stock if needed |
| `/return-to-warehouse` | `reason`, required `restock`, optional `warehouseId` | DeliveryFailed → Returned |

Customer cancellation remains limited to Pending orders. Staff cancellation is
allowed for Pending/Confirmed/Preparing/ReadyForDispatch, never after departure.
Reasons are trimmed, 1–400 characters. Returns apply only to undelivered failed
orders. Delivered/paid returns and cash/card refunds need a separate policy/workflow.
`restock: true` means operations has physically received and approved the goods;
false records a write-off and adds no inventory. Changing a completed return's
decision on replay returns 409. Repeated cancellation/return does not add stock twice.
An alternate restock warehouse must be active; otherwise use/reactivate the original.

For redelivery, operations assigns a failed order again, after arranging physical
handover if changing drivers, then that driver dispatches. This neither replenishes
nor deducts warehouse stock a second time. Assigning the same driver to the same
unstarted delivery is idempotent. A driver can have only one Assigned/OutForDelivery/
Failed shipment at a time, enforced by SQL and transaction locks.

## Driver accounts and routes

`delivery.manage` protects `/api/v1/admin/delivery`:

- POST `/drivers`: `{ "userId": "<verified user GUID>", "name": "Courier name" }`.
- GET `/drivers?page=1&pageSize=20`.
- PUT `/drivers/{id}`: `name`, `isActive`, latest base64 `version`.

The user must already have verified their phone. Drivers sign in with ordinary
phone OTP; active driver membership is checked in SQL, not supplied in the request.
An active assignment blocks driver deactivation. Stale profile versions return 409.

| Driver route | Input / result |
| --- | --- |
| GET `/api/v1/driver/deliveries` | Own paged shipments only |
| POST `/api/v1/driver/deliveries/{orderId}/dispatch` | No body; starts assigned job |
| POST `/api/v1/driver/location` | Explicit latitude/longitude, valid ranges, ≤6 decimals |
| POST `/api/v1/driver/deliveries/{orderId}/complete` | operationId, deliveryCode, collectedAmount |
| POST `/api/v1/driver/deliveries/{orderId}/fail` | reason |
| GET `/api/v1/driver/cash` | Own unsettled cash total |

Non-drivers return 403; another driver's job returns 404. Customer contact/address
is shown to the assigned driver only while their job is active, and hidden from
completed/cancelled job history. Location updates allow one per five seconds;
429 includes Retry-After. These are device-reported locations, not GPS attestation.

Customer GET `/api/v1/orders/{id}/tracking` returns status, driver display name,
and location only while OutForDelivery, reported since departure and within five
minutes. Location is hidden after delivery/failure and from other customers.
Driver contact numbers are not automatically shared with the customer.

## Receipt proof and collection

Only the customer can GET `/api/v1/orders/{id}/delivery-code` while the order is
OutForDelivery. The app displays that six-digit code, which the customer gives the
driver at handover. It is encrypted at rest with Data Protection, not logged or
exposed in driver responses. It expires after an hour; five bad attempts lock it.
The customer can read again to rotate an expired/locked code. Incorrect attempts
commit even though delivery fails, so transaction rollback cannot bypass limits.

Completion example:

```json
{
  "operationId": "<new GUID retained for retries>",
  "deliveryCode": "123456",
  "collectedAmount": 40.00
}
```

Amount must exactly equal the order total. Successful handover atomically updates
shipment Delivered, order Delivered/Paid, COD receipt Collected, and notification.
Stock was consumed at confirmation and is not deducted again. Retry the original
operation ID/amount as the same driver; another ID or amount conflicts after success.
There is no external payment gateway transaction: this records cash received.

## Cash handover and receipts

Customer GET `/api/v1/payments/{orderId}` returns their own receipt (404 for others).
`payments.manage` protects:

- GET `/api/v1/admin/payments?status=Collected&page=1&pageSize=20`.
- GET `/api/v1/admin/payments/{orderId}`.
- GET `/api/v1/admin/payments/drivers/{driverId}/cash`.
- POST `/api/v1/admin/payments/settlements`:

  ```json
  {
    "operationId": "<new GUID retained for retries>",
    "driverId": "<driver GUID>",
    "orderIds": ["<delivered order GUID>"],
    "amountReceived": 40.00,
    "reference": "CASH-HANDOVER-001"
  }
  ```

This is an operations attestation of physical cash handover. Select 1–100 distinct
collected, unsettled orders from the same driver. Received amount must exactly
match their sum; reference is unique. The original operation ID/payload/actor
replays the receipt. Another operation cannot settle the same invoices twice.
The order remains Paid, while settlement removes its amount from outstanding cash.

## Notifications, staff and deployment

GET `/api/v1/notifications?page=1&pageSize=20` is the authenticated recipient's
in-app inbox. POST `/api/v1/notifications/{id}/read` is idempotent; another recipient
gets 404. SQL deduplication keys prevent duplicate messages on command replay.
The durable inbox is written in the same transaction as order/delivery/payment
changes. FCM/APNs push and email delivery are not connected; clients can poll the
inbox. SMS has a local simulator and a configurable HTTPS gateway adapter; a real
gateway account and its provider integration remain external prerequisites.

See [deployment](deployment.md) for production staff provisioning/exchange,
Docker and Kubernetes templates. Local-only operations tokens are not production
staff credentials. The core COD lifecycle is implemented; production rollout still
requires provider credentials, approved shipping settings, certificate/secrets,
registry/cluster configuration and deployment verification.

All eight modules own separate schemas/contexts/history in the same SQL database.
Order workflows enlist seven business contexts on one physical SQL transaction;
Identity authenticates separately. Extracting services requires a distributed
workflow, not just assigning separate databases. Use explicit migrations:

```powershell
dotnet run --project src/Velo.Api --no-build --launch-profile http -- --migrate true
```

This command applies all eight contexts, exits without starting HTTP, and does not
seed admin/driver/customer/demo records. Readiness stays unhealthy while any
migrations are pending; liveness remains independent of SQL readiness.

For maintainers, `tools/postman/Verify-FullCycle.ps1` creates and drops only its
own `velo_postman_tests_<guid>` database, forces simulated OTP, starts a separate
test API, and executes this exact collection's scripts/requests through the small
Velo-specific Node runner. It does not implement the full Postman scripting API.
