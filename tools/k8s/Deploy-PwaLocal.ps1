[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Invoke-Kubectl {
    $output = $input | & kubectl --context docker-desktop @args
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." }
    $output
}

# Reuse the Velo ingress controller and TLS Secret.
Invoke-Kubectl get ingressclass velo-traefik -o name
Invoke-Kubectl get secret velo-wildcard-tls -n velo -o name
if (-not $SkipBuild) {
    & docker build -t velo-pwa:local (Join-Path $repoRoot 'apps/velo-pwa')
    if ($LASTEXITCODE -ne 0) { throw 'PWA image build failed.' }
}
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s/pwa-deployment.yaml')
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s-local/pwa-ingress.yaml')
# Recreate pods to pick up a rebuilt local image with the same tag.
Invoke-Kubectl rollout restart deployment/velo-pwa -n velo
Invoke-Kubectl rollout status deployment/velo-pwa -n velo --timeout=180s
Invoke-Kubectl get 'pods,service,ingress' -n velo
Write-Host 'Velo demo: https://app.velo.internal (requires its hosts entry and certificate trust).'
