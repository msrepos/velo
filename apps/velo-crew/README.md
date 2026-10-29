# Velo Crew

Standalone minivan staff application. Each minivan is a Mobile warehouse with
its own unique warehouse UUID and top 100 selling product assortment.

This project has its own React entry point, dependency lockfile, image,
Kubernetes Deployment/Service and HTTPS ingress. No imports from the storefront
or admin application are used. `/admin` and `/admin/*` return 404. It shares the
authenticated Velo backend API; existing `orders.manage` permission remains
cross-warehouse and is not a per-van assignment boundary.

## Development

```powershell
cd apps/velo-crew
pnpm install --frozen-lockfile
pnpm dev
pnpm test
pnpm build
```

Open `http://127.0.0.1:5176/` to sign in or `/?demo=1` for the interactive demo.
Configure `VELO_API_PROXY_TARGET` if using a different local API.

Staff require `orders.manage`. With `inventory.manage`, they can select an
active Mobile warehouse; otherwise enter the UUID supplied by the operator.
Orders poll every 15 seconds and are filtered by warehouse before pagination.
Workflow: Receive order → Start preparing → check all quantities → Mark prepared.
Order status is persisted by the API. Picking counts stay in sessionStorage
per staff member, van and order for the current browser tab; counts are not
shared across devices or validated by the backend. Demo order state resets
on reload. Live location, driver ETA, ranking and replenishment are outside
the preparation screen. Travel and preparation are separate concerns.

## Docker Desktop Kubernetes

From the repository root:

```powershell
./tools/k8s/Deploy-CrewLocal.ps1
```

URL: `https://crew.velo.internal/`. Map `127.0.0.1 crew.velo.internal` in Windows
hosts. Uses the existing Velo ingress controller and wildcard TLS certificate.
Two replicas run as a non-root user with a read-only filesystem and health probes.
API traffic is proxied at the same origin to `velo-api` without caching responses.
No admin frontend is included in the image. Backend routes under `/api/v1/admin`
retain their existing server permission checks and are used for staff operations.
