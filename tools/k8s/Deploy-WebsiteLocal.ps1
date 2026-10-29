[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Invoke-Kubectl {
    & kubectl --context docker-desktop @args
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." }
}

Invoke-Kubectl get ingressclass velo-traefik -o name
Invoke-Kubectl get secret velo-wildcard-tls -n velo -o name
if (-not $SkipBuild) {
    & docker build -t velo-website:local (Join-Path $repoRoot 'apps/velo-website')
    if ($LASTEXITCODE -ne 0) { throw 'Website image build failed.' }
}
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s/website-deployment.yaml')
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s-local/website-ingress.yaml')
Invoke-Kubectl rollout restart deployment/velo-website -n velo
Invoke-Kubectl rollout status deployment/velo-website -n velo --timeout=180s
Write-Host 'Velo website: https://velo.internal (requires its hosts entry and certificate trust).'
