# Velo official website

Independent, dependency-free marketing website for `https://velo.internal`.
The customer application remains at `https://app.velo.internal`.

## Develop and validate

Requires Node.js 24. No dependency installation is required.

```powershell
cd apps/velo-website
node server.mjs
# http://127.0.0.1:5180
node --test website.test.mjs
```

`build.mjs` creates `dist/` with separate Home (`/`), Fleet (`/fleet/`),
App (`/app/`), Contact (`/contact/`) and 404 pages. Templates and shared contact
configuration live in `build.mjs`; CSS and browser interactions live in `public/`.
All imagery is served locally, using brand assets from the supplied Velo reference.
Fleet images and the app mockup are illustrative. No third-party scripts or fonts
are required.

The contact form validates fields and prepares a `mailto:` message in the visitor's
email application. It does not submit or store messages. Contact details currently
match the supplied reference: `info@veloapp.ia`, `+20 102 000 0115`.
Update the exported `contact` configuration in `build.mjs` to change them.
Native iOS/Android store links have not been supplied; app CTAs open the actual web app.

## Deploy to local Kubernetes

Run from the repository root:

```powershell
./tools/k8s/Deploy-WebsiteLocal.ps1
```

The script builds `velo-website:local`, applies only the website Deployment,
Service and TLS Ingress, and waits for both replicas to become ready. It uses
the existing `velo-traefik` ingress class and `velo-wildcard-tls` secret in `velo`.
The Windows hosts entry `127.0.0.1 velo.internal` and client trust for the local
certificate must already exist. The website is internal and is not published to
an external hosting provider. Full-stack Kustomize deployments also include it;
build its image first.
