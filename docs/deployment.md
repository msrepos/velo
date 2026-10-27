# Containers and Kubernetes

The modular monolith runs as one API image. SQL Server remains the shared database;
module folders are not separately deployable services. Multiple API replicas use
SQL-backed locks, authentication sessions and Data Protection keys. Configure the
same Data Protection certificate on all replicas.

## Local Docker: API and SQL Server together

Start Docker Desktop with Linux containers, then run from the repository root:

```powershell
./tools/docker/Start-Local.ps1
# If port 5080 is occupied:
# ./tools/docker/Start-Local.ps1 -Port 5081
```

The script creates random SQL and OTP secrets once in the ignored
`infrastructure/docker/.env.local`, builds the API image, starts an isolated
SQL Server container, waits for SQL health, applies all module migrations, and
starts the API. It verifies `http://localhost:5080/health/ready` before reporting
success. SQL Server is internal to the Docker network and stores data in the
`velo-local_sqlserver_data` volume. This stack does not use the host's database.
Keep `.env.local` when restarting: its password must match the persistent database.
The initial SQL image download and API build can take several minutes.

To inspect or stop this stack (data is retained):

```powershell
docker compose --env-file infrastructure/docker/.env.local logs --tail 100 api migrator
docker compose --env-file infrastructure/docker/.env.local ps -a
docker compose --env-file infrastructure/docker/.env.local down
```

For direct Compose use after the script has created the secrets:

```powershell
docker compose --env-file infrastructure/docker/.env.local up --build -d --wait
Invoke-WebRequest http://localhost:5080/health/ready
```

This is a local Development setup with simulated OTP and SQL Server Developer
edition. Production prerequisites are listed below.

## Local Docker with existing SQL Server

Docker Desktop must be running Linux containers. From the repository root:

```powershell
Copy-Item infrastructure/docker/.env.api.example infrastructure/docker/.env.api
# Edit .env.api: set your SQL connection and a base64 key of at least 32 random bytes.
docker compose --env-file infrastructure/docker/.env.api -f infrastructure/docker/compose.api.yml build
docker compose --env-file infrastructure/docker/.env.api -f infrastructure/docker/compose.api.yml run --rm migrator
docker compose --env-file infrastructure/docker/.env.api -f infrastructure/docker/compose.api.yml up -d api
```

Use `host.docker.internal` to reach Windows SQL Server from the Linux container.
Stop the Visual Studio API first if it uses port 5080, or change VELO_HTTP_PORT.
This compose configuration is local Development only, binds to loopback and exposes
simulated OTP codes. It does not start another SQL Server. The original compose.yml
remains available for users who want a separate SQL Server container.

The image runs as the .NET image's non-root application user, listens on 8080,
and excludes appsettings.Local.json, .env files and build/user artifacts from the
build context. The compose API filesystem is read-only with a temporary /tmp.
Credentials are injected at runtime. `/health/live` checks only process liveness;
`/health/ready` verifies all eight contexts have their migrations applied.

## Local Docker Desktop Kubernetes

For local Kubernetes with simulated OTP, use the explicit Development overlay.
The existing `velo-runtime`, `velo-migrations`, and `velo-dataprotection` Secrets
must already be provisioned in namespace `velo`, and `velo-api:local` must be
available to Docker Desktop. The configured SQL Server runs outside this cluster;
it does not need a SQL Server pod.

```powershell
docker build -t velo-api:local .
./tools/k8s/Initialize-LocalIngress.ps1
./tools/k8s/Deploy-Local.ps1
Invoke-WebRequest https://api.velo.internal/health/ready
```

The script requires context `docker-desktop`, applies the local configuration,
runs a uniquely named migration Job and waits for completion before deploying
and restarting the two API replicas. A completed Job does not run again when
reapplied. The migrator uses the same API image with `--migrate true`; it exits
after checking/applying migrations for all eight modules, so no permanent migrator
Deployment or Service is needed. Completed migration pods can be cleaned up;
the Job status records completion. Inspect a retained run with
`kubectl logs -n velo job/<job-name>`.

The `infrastructure/k8s-local` overlay selects Development, the Development SMS
provider, and exposed simulated OTP codes. Run `Initialize-LocalIngress.ps1` with
PowerShell 7 to install the dedicated Traefik ingress controller and create the
`velo-wildcard-tls` Secret. The self-signed certificate covers `*.velo.internal`
and `velo.internal`, expires after one year, and is reused on subsequent runs.
Certificate/key files live under ignored `.local/certificates`; keep the private
key private. To renew, archive the pair and rerun initialization.

The two Ingress resources route `https://api.velo.internal` to `velo-api:80` and
`https://app.velo.internal` to `velo-pwa:80`. There is no ingress for `velo.internal`;
it is reserved for the future website. Add `127.0.0.1 app.velo.internal api.velo.internal`
to Windows hosts. Trust `.local/certificates/velo.internal.crt` in your client's
trusted certificate store for browser/PWA use, or use curl's `--cacert` option.
The TLS Secret imports the certificate into ingress; it does not establish client trust.

Velo uses HTTPS port 443. The existing WSO2 Envoy Gateway in namespace `apim`
now uses port 8443 for all five HTTPS listeners, for example
`https://am.wso2.com:8443`. Preserve that port in any future WSO2 gateway apply.
The shared Envoy controller remains for WSO2; the Velo Gateway and HTTPRoutes
are removed. No HTTP listener is exposed by the Velo ingress controller.

It retains the existing SQL credentials
and Data Protection certificate. Production configuration remains in
`infrastructure/k8s`; applying that base selects HttpGateway and requires a real
HTTPS gateway URL and API key. `https://velo-api/otp` is not an SMS gateway
implemented by this API. The base/overlay exclude Secrets and the migration Job
from bulk apply so credentials are managed separately and migrations can finish
before rollout.

## Customer PWA on local Kubernetes

The demo frontend is containerized independently from the API. Build and deploy
from the repository root after initializing local ingress:

```powershell
./tools/k8s/Deploy-PwaLocal.ps1
```

Its source Dockerfile is `apps/velo-pwa/Dockerfile`; the Deployment/Service are in
`infrastructure/k8s/pwa-deployment.yaml` and the TLS Ingress is in
`infrastructure/k8s-local/pwa-ingress.yaml`. The complete local Kustomize overlay
also includes these resources. Build both `velo-api:local` and `velo-pwa:local`
before using the full-stack `Deploy-Local.ps1` script.

Browse the frontend at `https://app.velo.internal` after configuring hosts and
trusting the self-signed certificate. This remains a sample-data demo with no
live purchases or backend integration.

## Production prerequisites

The files in infrastructure/k8s are templates, not an already deployed cluster.
Set your registry/release tag, real API host, SQL logins, OTP key, HTTPS SMS gateway
and Data Protection certificate. Runtime and migration SQL credentials are separate:
the migration login needs DDL privileges; the application login should have only
the required application/data permissions (including SQL application locks).

The SMS HttpGateway adapter posts `{"phoneNumber":"+...","code":"123456"}` over
HTTPS with `Authorization: Bearer <GatewayApiKey>`, uses a ten-second timeout and
rejects redirects/non-2xx responses. Your gateway must implement that contract and
connect to your chosen SMS provider. No real gateway/provider account is supplied.
Development SMS/codes are rejected outside Development. Production also requires
a PFX certificate and password to protect SQL-persisted Data Protection keys.
Do not replace these requirements with simulated production authentication.

Create namespace/config/secrets through your normal secret-management process.
Create `velo-dataprotection` containing `identity.pfx`. Run the release-specific
migration Job and wait for success before updating the Deployment/Service. Replace
all REPLACE placeholders first. Do not apply secrets.example.yaml as-is. Configure
your own TLS ingress/certificate for external traffic; the supplied Service is
internal. No image push, cluster deployment, external SMS or payment-provider call
is performed by generating these files.

Checkout is disabled in the production ConfigMap until the operator explicitly
approves shipping rates/radius and enables it. The 20 EGP/5 km values are examples.
The implemented payment method is cash on delivery. Card gateways/refunds and
FCM/APNs push require separate provider integration; in-app notifications work
through the authenticated inbox API.

## Staff access

Provision only an active verified user from a server terminal:

```powershell
dotnet run --project src/Velo.Api --no-build --launch-profile http -- --grant-staff-permissions <user-GUID> --permissions orders.manage,delivery.manage,payments.manage
dotnet run --project src/Velo.Api --no-build --launch-profile http -- --revoke-staff-permissions <user-GUID>
```

In production use `dotnet Velo.Api.dll` with the same arguments and production
configuration. Grant/revoke invalidates existing sessions. Sign in again through
OTP, then POST `/api/v1/auth/staff/exchange` using that customer token within five
minutes of OTP verification. The server reads its permission allowlist; client
permissions are ignored. The separate staff session lasts at most eight hours,
supports normal refresh/logout, and refresh reloads trusted permissions. Ordinary
customer OTP/refresh never automatically acquire staff permissions. GET
`/api/v1/auth/staff/me` returns the granted permissions for a staff session.

Development-only `--issue-development-operations-token <user-GUID>` remains a
15-minute convenience token for local Postman, with all five management permissions.
It is not a production provisioning mechanism. There is no default admin password
or automatically seeded staff/driver account.

