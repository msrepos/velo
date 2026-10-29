# Velo customer PWA

A React + TypeScript + Vite shopping PWA, using Velo's red identity and
Madinaty delivery context. Products, categories, SKU prices, descriptions and images
load from the public Catalog API. Each active SKU is a separate purchasable card.
Delivery estimates, profile, checkout and tracking remain simulated; no SMS,
payments or real orders are sent. Cart, favorites, profile and demo orders are
stored in this browser's localStorage.

Customers can add, edit, and remove labeled delivery addresses in their profile,
choose a default, and select a different saved address for checkout. The previous
single saved address is migrated automatically. Removing the default makes the
first remaining address the default. Addresses and preferences stay on this device.

The profile supports a preferred payment method: Cash on delivery (COD), Card,
or InstaPay. Checkout starts with that preference and saves the selected method
on each demo order. InstaPay shows recipient **+201020000115**. No transfer is
requested or verified in this demo. Card is a preview selection; no card details
are collected or stored. Live card charging and transfer verification require a
payment provider and server integration.

## Run

Use Node.js 22.12+ and the package manager pinned in package.json:

```powershell
pnpm install
pnpm dev
pnpm test
pnpm build
pnpm preview --port 5173
```

Open the printed localhost URL. The production build includes the PWA manifest,
192/512 px icons, a maskable icon, and a Workbox service worker that caches the
interface and illustrations. Service workers are enabled in production preview,
not Vite development. Serve over HTTPS for installation on devices; localhost
is supported for desktop development. A phone cannot access the PC's localhost.

## Visual Studio

Open `Velo.slnx` and expand `apps > velo-pwa`. The JavaScript project uses pnpm
for dependency restore, build, and tests. Install Node.js 22.12+ and pnpm 11.25.0
on your PATH, with Visual Studio's JavaScript/TypeScript project support enabled.
Set `velo-pwa` as the startup project to run the Vite development server with
`pnpm dev`, then open the localhost URL printed in its output.

## Admin workspace

Open `https://app.velo.internal/admin` for the staff interface. Deep links include
`/admin/products`, `/admin/categories`, `/admin/orders`, and `/admin/users`.
Both the storefront catalog and admin workspace use the live API.
The desktop sidebar links to all five sections; smaller screens use the menu
button. Admin layouts use `velo-admin-` class names to avoid generic advertising
filters hiding the sidebar or page content. Navigation to `/admin` bypasses the
customer service worker's offline HTML fallback, and the workspace offers an
update notice when a new service worker is waiting. After upgrading from the
original admin build, use Ctrl+Shift+R once to replace a cached shell.

- Overview: live record totals, recent orders, and pending confirmation count.
- Catalog: create/edit products and categories, activate/deactivate records, and
  create/edit SKU names, codes, barcodes, prices, and active state.
- Orders: filter by lifecycle status, inspect customer/address snapshots, items,
  payment state and history; confirm, prepare, mark ready, or cancel with a reason.
  Driver assignment, delivery proof, and returns remain in the operations API.
- Users: register accounts with name, email, phone, and Customer/Admin/Driver/Crew
  role; search accounts, edit profiles/roles, and activate/deactivate users. Driver
  and Crew roles automatically maintain their fleet profiles. New accounts verify
  their phone by OTP on first sign-in. Role changes and deactivation revoke sessions;
  operators cannot deactivate themselves or change their own role.

Sign in using an existing verified user's phone number and OTP. With the local
simulated SMS provider, the server's development code appears in the sign-in form.
Customer login alone does not grant staff access. Existing administrators can
register users or assign Admin/Crew roles in Users. To bootstrap the first admin,
an operator can provision a verified user from the repository root:

```powershell
dotnet run --project src/Velo.Api --launch-profile http -- --grant-staff-permissions <verified-user-guid> --permissions catalog.manage,orders.manage,users.manage
```

Provisioning replaces that user's staff permissions and revokes their sessions.
Sign in again afterward. Terminal provisioning supports custom permission sets;
the browser role selector applies the standard role permissions.
The browser exchanges the freshly verified customer session for a staff session,
revokes the temporary customer session, and automatically refreshes staff tokens.
Tokens live only in memory: reloads require sign-in. No tokens or admin records are
saved in browser storage or service-worker API caches.

Nginx forwards `/api/` to the cluster's `velo-api` service on the same origin.
For Vite development, `/api/` proxies to `https://api.velo.internal`; Node must trust
the local certificate authority. To use the .NET HTTP launch profile instead,
create `apps/velo-pwa/.env.local` containing
`VELO_API_PROXY_TARGET=http://localhost:5080`, start the API with
`dotnet run --project src/Velo.Api --launch-profile http`, then restart Vite.
No CORS relaxation is required. Rebuild both API
and PWA images to deploy this feature; no new database migration is required.

User API: `GET /api/v1/admin/users?search=&page=1&pageSize=20`,
`POST /api/v1/admin/users`, and `PUT /api/v1/admin/users/{id}` all require Staff +
`permission=users.manage`. POST accepts `name`, `email`, `phoneNumber`, `role`, and
optional `isActive`. PUT accepts `name`, `email`, `isActive`, optional `role`, and
the latest response's `version`. Omitting role preserves existing access. Responses
include the selected account role. A stale version returns 409. Role/profile changes
commit atomically across Identity and Delivery; active shifts/deliveries block
removing operational membership. See [user registration](../../docs/user-registration.md).

## Fleet administration

Admin includes `/admin/drivers`, `/admin/minivans`, `/admin/crew`, and
`/admin/dispatching`. Register drivers/crew against verified user accounts, reuse
Mobile warehouse records for minivans, and start/end shifts containing a driver,
van, and crew member. See [fleet setup, API, permissions, and migration](../../docs/fleet-dispatching.md).
Deploy the API and apply the new Delivery migration before using the fleet pages.
All four pages require `delivery.manage`; minivan writes also require
`inventory.manage`, and account search requires `users.manage`.

## Customer journeys

- Search, filter by category, sort by price, and inspect product details.
- Save favorites; add, remove, and change bag quantities (maximum 20 per item).
- Save a demo Madinaty address; validate an Egyptian mobile phone format.
- Review the 20 EGP demo delivery fee and place a simulated cash-on-delivery order.
- Preview four delivery stages with an explicit simulation button.
- Reload with retained local state; open the cached interface offline after a
  successful online visit; checkout requires a connection.
- Home-screen installation help and browser install prompt where available.
- Service-worker update prompt; applying an update is disabled during checkout.

## Catalog integration

The storefront requests `/api/v1/catalog/categories` and `/api/v1/catalog/products`
through the same-origin `/api/` proxy. It fetches all pages (100 records per page),
then applies search, category filters and price sorting locally. Only published
products with active variants are shown; publication does not imply inventory
availability. Empty API results show empty shelves; failed requests show retry
controls. No sample products are substituted. Missing or failed images show a
neutral placeholder. Reload or reconnect to refresh catalog data.

Bag lines and favorites use variant UUIDs. After a successful catalog load, bag
lines no longer present in the catalog are removed and totals use current prices.
Failed requests preserve saved bag data. Public catalog responses use no-store
and are not cached by the service worker; a cold offline start has no catalog.
The API and its database must be running. Create an active category, product and
at least one active SKU in `/admin` to publish the first product.

## Backend integration next

Replace local checkout with typed clients for the customer cart and orders APIs.
Define browser authentication/session handling and cross-origin configuration
before connecting OTP. Confirm stock, price, delivery coverage and totals on the
server; use order idempotency. Do not cache authentication, private API responses,
or checkout mutations. Web Push needs separate server subscription and delivery
support. The current demo doesn't claim real-time inventory or notifications.

## Local Docker Desktop Kubernetes

From the repository root, with Docker Desktop running Linux containers and the
Velo ingress controller initialized:

```powershell
./tools/k8s/Deploy-PwaLocal.ps1
```

This builds `velo-pwa:local` using a Node build stage and an unprivileged Nginx
runtime, then deploys two PWA replicas, an internal Service and a TLS Ingress.
The runtime has a read-only filesystem with writable temporary storage, health
probes and CPU/memory limits. The storefront catalog requires the backend; checkout remains simulated.

Manual equivalent:

```powershell
docker build -t velo-pwa:local apps/velo-pwa
kubectl --context docker-desktop apply -f infrastructure/k8s/pwa-deployment.yaml
kubectl --context docker-desktop apply -f infrastructure/k8s-local/pwa-ingress.yaml
kubectl --context docker-desktop rollout restart deployment/velo-pwa -n velo
kubectl --context docker-desktop rollout status deployment/velo-pwa -n velo
```

Initialize ingress first with `./tools/k8s/Initialize-LocalIngress.ps1` (PowerShell 7).
Add `127.0.0.1 app.velo.internal api.velo.internal` to Windows hosts.
Open `https://app.velo.internal`; the API is `https://api.velo.internal`.
`velo.internal` is reserved for the future website. Trust the public certificate
at `.local/certificates/velo.internal.crt` for browser/PWA installation and offline
features. Both ingresses reference the `velo-wildcard-tls` Secret.

Nginx serves the SPA fallback, gives hashed assets immutable caching and ensures
the HTML and service worker revalidate. All future work on this demo stays local;
the earlier private Sites deployment is not used by Docker or Kubernetes.

## Minivan staff workspace

Crew is a separate application in `apps/velo-crew`, deployed at
`https://crew.velo.internal/`. The PWA's former `/staff` route redirects there;
its build no longer contains crew code. Use `https://crew.velo.internal/?demo=1`
for interactive sample orders. Staff sign in by OTP with `orders.manage` permission.
Select an active Mobile warehouse (lookup requires `inventory.manage`) or enter
its warehouse UUID supplied by the operator. Each minivan is a moving warehouse
with its own ID and an assortment of the top 100 selling products; assortment
ranking and replenishment are outside this preparation screen.

The queue polls every 15 seconds and filters by warehouse on the API before
pagination. The workflow is Pending → Receive order (Confirmed) → Start preparing
(Preparing) → Mark as prepared (ReadyForDispatch). The UI requires every quantity
to be checked before completion. Picking counts are saved in sessionStorage per
staff member, van and order, for the current browser tab; they are not shared
across devices or validated by the API. Order status is persisted by the existing
authenticated API. Existing `orders.manage` grants access across warehouses;
van selection is an operational filter, not a per-van authorization boundary.

Preparation and van travel can happen in parallel. Live location, driver ETA,
automatic driver departure, delivery completion, and stock replenishment are
not part of this screen. Demo journey data is labeled as sample data. Demo order
state resets on reload or with Reset demo. The application never sets delivery
status when staff mark an order prepared.

