# Customer addresses and cart

This describes saved delivery addresses and a persisted customer cart.
[COD checkout](orders-api.md) now adds order creation, a fee/total preview,
radius-based warehouse selection and reservations. Online payments are pending.
A cart itself does not reserve stock
or guarantee a price or availability for a future order.

Use the normal customer access token from OTP verification/refresh. Every route
requires authentication and derives the customer ID from the validated session.
There is no client-supplied customer ID. Another customer's address returns 404
for read/update/delete; carts are always scoped to the current user. Responses
have `Cache-Control: no-store`. Missing/revoked/expired tokens return 401.

## Migrations and layout

Customers owns `customers.Addresses` and `CustomersDbContext`; Orders owns
`orders.Carts`, `orders.CartItems` and `OrdersDbContext`. Each has its own
`__EFMigrationsHistory`. IDs referring to Identity/Catalog are module references,
without cross-schema foreign keys. Orders reads current offers through
`ICatalogOffers` in Catalog.Contracts, without accessing Catalog tables directly.

Run from the solution root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --context CustomersDbContext --project src/Modules/Customers/Velo.Modules.Customers --startup-project src/Velo.Api
dotnet ef database update --context OrdersDbContext --project src/Modules/Orders/Velo.Modules.Orders --startup-project src/Velo.Api
```

Visual Studio Package Manager Console:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Update-Database -Context CustomersDbContext -Project Velo.Modules.Customers -StartupProject Velo.Api
Update-Database -Context OrdersDbContext -Project Velo.Modules.Orders -StartupProject Velo.Api
```

Apply Identity, Catalog and Inventory migrations too when setting up a new
database. Migrations run explicitly, never on API startup. Reviewed idempotent
scripts are `infrastructure/database/customers.sql` and `orders.sql`.
`verify-customer-cart.sql` checks the schema without changing data.

## Addresses

Base path: `/api/v1/customers/me/addresses`.

| Method | Path | Result |
| --- | --- | --- |
| GET | base path | Array of own addresses, default first |
| POST | base path | Create, returns 201 and Location |
| GET | `/{id}` | Read own address |
| PUT | `/{id}` | Replace fields, requires latest body `version` |
| DELETE | `/{id}` | Delete own address, returns 204 |

Example POST:

```json
{
  "label": "Home",
  "recipientName": "Mohamed Farag",
  "phoneNumber": "+201012345678",
  "city": "Cairo",
  "area": "Nasr City",
  "street": "Example street",
  "building": "10",
  "floor": "2",
  "apartment": "4",
  "instructions": "Call on arrival",
  "latitude": 30.050000,
  "longitude": 31.350000,
  "isDefault": true
}
```

All fields above except floor/apartment/instructions/isDefault are required.
Coordinates must be supplied explicitly, within latitude ±90 / longitude ±180,
with at most six decimal places. Coordinates alone do not establish delivery
coverage. The recipient phone must be `+` followed by 8–15 digits, starting
with a nonzero country code. This contact phone does not change the login phone
and is not separately OTP verified.

Limits: label 80, recipientName 160, city 100, area 160, street 300, building 80,
floor/apartment 40 and instructions 500 characters. Text is trimmed; blank required
values and control characters are rejected. A customer can save up to 20 addresses.

The first saved address becomes default automatically. Setting `isDefault: true`
on another address switches the default atomically. `false` preserves a current
default; select another address to switch it. Deleting the default promotes the
oldest remaining address. SQL enforces at most one default, and mutations use
a transaction lock per customer to handle concurrent devices/API instances.

PUT uses the same JSON plus `version` from the latest response (base64 SQL
rowversion). Missing/invalid version returns 400; a stale version returns 409
`address_version_conflict`. Switching the default also invalidates the previous
default's version. POST is not idempotent: after an uncertain response, check the
address list before creating another copy. DELETE does not require a version;
repeating deletion returns 404.

## Cart

Base path: `/api/v1/cart`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | base path | Read cart with current Catalog prices |
| PUT | `/items/{variantId}` | Set absolute quantity |
| DELETE | `/items/{variantId}` | Remove variant |
| DELETE | base path | Clear all items |

GET returns a JSON `version` and the quoted value in the `ETag` response header.
For an untouched cart this is `"empty"`; GET does not create a database row.
Send the complete ETag in **If-Match** for every PUT/DELETE:

```http
PUT /api/v1/cart/items/<variant-guid>
Authorization: Bearer <customer-access-token>
Content-Type: application/json
If-Match: "<latest-version>"

{"quantity": 3}
```

Quantities are absolute counts, 1–1000 per distinct variant, with up to 100
variants per cart. Use the Catalog **variantId**, not productId. Setting an existing
variant replaces its quantity; it does not increment it. Price/subtotal supplied
by a client are ignored. Unknown or inactive variants/products/categories cannot
be added or updated (409 `variant_unavailable`). Existing unavailable items remain
visible and removable. Removing a missing item is a successful no-op with a new
version. A clear retains the cart header to prevent older versions from becoming
valid again.

Successful operations return 200 with the updated cart and ETag. The response
contains `currency: "EGP"`, `subtotal`, `hasUnavailableItems` and `items`, each
with quantity, product/variant names, SKU, current unitPrice, lineTotal and
isAvailable. Subtotal becomes null if any item is unavailable; remove or resolve
those items. Taxes/discounts/delivery charges are not calculated in this milestone.
IsAvailable means published in Catalog, not stock availability.

Missing If-Match returns **428** `cart_version_required`. A stale, unquoted,
wildcard or mismatched version returns **412** `cart_version_conflict`. After
412, GET the cart and let the client reconcile before resending. After a timeout,
GET first to determine whether the absolute quantity change succeeded.
Always save the response's latest ETag after a write. The version includes the
cart rowversion and current offer data, so a changed price/name/publication also
invalidates the earlier view. This is not a price lock for checkout.

## Postman

For addresses only, import `tools/postman/Velo.Customers.postman_collection.json`.
Set `baseUrl` and the ordinary customer `accessToken`, then run 01–08. It creates,
updates and deletes its demo address; it does not touch the cart or need a variant.
Requests 06 (409 stale version) and 08 (404 deleted address) are expected.

Import `tools/postman/Velo.CustomerCart.postman_collection.json`. Set `baseUrl`,
`accessToken` (normal customer token) and `variantId` (an active Catalog variant).
Use No environment to avoid overriding collection variables. Requests run in
order, save the address version and cart ETag, and demonstrate stale edits.
The collection **clears the logged-in customer's cart**, creates/updates/deletes
a demo address and ends with an empty cart. Other saved addresses remain; the
demo address does not explicitly request becoming default. If the user has no
addresses, it becomes default while it exists.
