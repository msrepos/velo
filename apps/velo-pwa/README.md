# Velo customer PWA demo

A React + TypeScript + Vite shopping prototype, using Velo's red identity and
Madinaty delivery context. The catalog, prices, availability, delivery estimates,
profile and order tracking are **demo data**. No live API requests, SMS, payments,
or real orders are sent. Cart, favorites, profile and demo orders are stored in
this browser's localStorage. Do not enter sensitive personal information.

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

## Included journeys

- Search, filter by category, sort by price, and inspect product details.
- Save favorites; add, remove, and change bag quantities (maximum 20 per item).
- Save a demo Madinaty address; validate an Egyptian mobile phone format.
- Review the 20 EGP demo delivery fee and place a simulated cash-on-delivery order.
- Preview four delivery stages with an explicit simulation button.
- Reload with retained local state; open the cached interface offline after a
  successful online visit; checkout requires a connection.
- Home-screen installation help and browser install prompt where available.
- Service-worker update prompt; applying an update is disabled during checkout.

## Backend integration next

Replace the sample catalog and local checkout with typed clients for the .NET API.
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
probes and CPU/memory limits. The demo remains independent from the backend.

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

