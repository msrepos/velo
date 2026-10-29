[CmdletBinding()]
param([switch]$SkipBuild, [string]$ImageTag = 'local')

$ErrorActionPreference = 'Stop'
if ($ImageTag -notmatch '^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$') { throw 'Invalid image tag.' }
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Invoke-Kubectl {
    & kubectl --context docker-desktop @args
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." }
}

Invoke-Kubectl get ingressclass velo-traefik -o name
Invoke-Kubectl get secret velo-wildcard-tls -n velo -o name
if (-not $SkipBuild) {
    & docker build -t "velo-crew:$ImageTag" (Join-Path $repoRoot 'apps/velo-crew')
    if ($LASTEXITCODE -ne 0) { throw 'Crew image build failed.' }
}
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s/crew-deployment.yaml')
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s-local/crew-ingress.yaml')
Invoke-Kubectl set image deployment/velo-crew "crew=velo-crew:$ImageTag" -n velo
if ($ImageTag -eq 'local') { Invoke-Kubectl rollout restart deployment/velo-crew -n velo }
Invoke-Kubectl rollout status deployment/velo-crew -n velo --timeout=180s
Write-Host 'Velo Crew: https://crew.velo.internal (requires hosts entry and existing certificate trust).'
