# VELO Postman auth cycle

For COD checkout/orders, import `Velo.Orders.postman_collection.json`. Set
`accessToken` (customer), `ordersStaffAccessToken`, `inventoryStaffAccessToken`
and an active Catalog `variantId`. Run 01–27 in order within 10 minutes with
No environment selected. The collection creates a located warehouse and receipt,
clears the customer's cart, previews the final total before each checkout, confirms
one order, cancels another and tests retries/address snapshots. Confirmed-order
cancellation intentionally returns 409. It leaves local stock movements/order history.
See `docs/orders-api.md` for token commands and settings. Use a test customer.

For customer addresses only, import `Velo.Customers.postman_collection.json`.
Set the ordinary customer `accessToken` and `baseUrl`, select No environment,
then run 01–08. It creates, updates and deletes its demo address without changing
the cart. Requests 06 (409) and 08 (404) are expected. No variantId or admin token
is needed.

For customer addresses and carts, import `Velo.CustomerCart.postman_collection.json`.
Set `accessToken` from ordinary OTP login and an active Catalog `variantId`.
Run 01–16 with No environment selected. Request 08 clears this customer's cart,
so use a test customer. The collection saves address versions/cart ETags,
creates/updates/deletes its demo address, and ends with an empty cart.
Requests 06 (409), 12 (412) and 16 (404) intentionally test rejected/stale requests.
See `docs/customer-cart-api.md` for API details and `Velo.Orders` for Checkout.

For Inventory, import `Velo.Inventory.postman_collection.json`. Set
`staffAccessToken` to an Inventory management token and `variantId` to an active
Catalog variant ID (not a product ID). See `docs/inventory-api.md` for the local
token command. Run 01–19 in order within 10 minutes. This creates two unique
warehouses, receives 20 units, tests retries, reserves/releases/commits stock,
and transfers 3 units. Final on-hand is 13 in the source and 3 in the target.
All checks should pass. Catalog and Inventory tokens carry separate permissions.
Select No environment to avoid overriding collection variables.

For Catalog management, import `Velo.Catalog.postman_collection.json`. Obtain a
temporary development staff token using the terminal command in
`docs/catalog-api.md`, set its `staffAccessToken` variable, then run 01–13.
Customer OTP tokens cannot manage Catalog. The collection creates demo catalog
data and demonstrates price updates, deactivation and reactivation. Steps 04 and
10 intentionally expect 404; all other checks should pass.

1. Run Velo.Api using the Visual Studio `http` profile (Development, port 5080).
2. Import `Velo.Auth.postman_collection.json` into Postman.
   Select **No environment** to avoid overriding the included collection variables.
3. Open the collection and run requests 01–10 in order, or use Collection Runner.
   No environment import is required; the collection includes its variables.
4. Request 02 chooses a new test phone by default and sends `phoneNumber`, `email`
   and `name` to `/api/v1/auth/register`. Edit name/email in collection variables.
   Request 03 creates the user
   after OTP verification and logs them in. Scripts capture the development code,
   access/refresh tokens and IDs without logging them to the console.
5. Requests 09 and 10 must return 401: they prove logout revoked access and refresh.

To sign in to the same user later, set collection variable `generateNewPhone`
to `false` and set `phoneNumber` to the same international number. Wait at least
60 seconds since the last OTP request for that phone, then repeat requests 02–07.
Registration does not overwrite existing users' profile fields. For a plain login,
`POST /api/v1/auth/otp/request` needs only the phone, followed by OTP verification.
There is no password endpoint.

The collection writes actual demo users and sessions into the configured local
database. SMS delivery is simulated. A 429 means a configured request/attempt
limit was reached; honor Retry-After. OTP codes expire after five minutes.
Always replace both tokens after refresh; reusing an old refresh token revokes
the session. Keep generated tokens local; do not export/share populated variables.
