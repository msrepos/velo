[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$overlay = Join-Path $repoRoot 'infrastructure/k8s-local'
$migration = Join-Path $repoRoot 'infrastructure/k8s/migration-job.yaml'

function Invoke-Kubectl {
    $output = $input | & kubectl @args
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." }
    $output
}

if ((Invoke-Kubectl config current-context) -ne 'docker-desktop') {
    throw 'This simulated OTP setup requires the docker-desktop Kubernetes context.'
}
Invoke-Kubectl get ingressclass velo-traefik -o name

# Secrets/certificate and velo-api:local, velo-pwa:local, velo-crew:local, velo-website:local images must already be provisioned.
# Preserve the existing certificate so existing authentication keys remain readable.
foreach ($secret in @('velo-runtime', 'velo-migrations', 'velo-dataprotection', 'velo-wildcard-tls')) {
    Invoke-Kubectl get secret $secret -n velo -o name
}

$resources = Invoke-Kubectl apply -k $overlay --dry-run=client -o json | ConvertFrom-Json
foreach ($resource in $resources.items | Where-Object { $_.kind -in @('Namespace', 'ConfigMap') }) {
    $resource | ConvertTo-Json -Depth 100 | Invoke-Kubectl apply -f -
}

# Recreate the completed singleton Job so every deployment still runs migrations.
# Refuse to replace unfinished/failed runs: preserve their pods and logs for diagnosis.
$job = Invoke-Kubectl create -f $migration --dry-run=client -o json | ConvertFrom-Json
$migrationJobs = @((Invoke-Kubectl get jobs -n velo -o json | ConvertFrom-Json).items |
    Where-Object { $_.metadata.name -match '^velo-migrate(?:-[a-z0-9]+)?$' })
foreach ($existing in $migrationJobs) {
    $completed = @($existing.status.conditions | Where-Object { $_.type -eq 'Complete' -and $_.status -eq 'True' }).Count -gt 0
    if (-not $completed) {
        throw "Migration Job '$($existing.metadata.name)' is unfinished or failed. Inspect its logs and resolve it before deploying."
    }
}
if ($migrationJobs | Where-Object { $_.metadata.name -eq $job.metadata.name }) {
    Invoke-Kubectl delete job $job.metadata.name -n velo --cascade=foreground --wait=true --timeout=60s
}
$job | ConvertTo-Json -Depth 100 | Invoke-Kubectl create -f -
try {
    Invoke-Kubectl wait -n velo --for=condition=complete "job/$($job.metadata.name)" --timeout=300s
}
finally {
    Invoke-Kubectl logs -n velo "job/$($job.metadata.name)" --all-containers=true --tail=60
}

# Remove successful legacy runs only after the new migration succeeds.
foreach ($existing in $migrationJobs | Where-Object { $_.metadata.name -ne $job.metadata.name }) {
    Invoke-Kubectl delete job $existing.metadata.name -n velo --cascade=foreground --wait=true --timeout=60s
}

Invoke-Kubectl apply -k $overlay
# ConfigMap environment variables are loaded only when a container starts.
Invoke-Kubectl rollout restart deployment/velo-api -n velo
Invoke-Kubectl rollout status deployment/velo-api -n velo --timeout=300s
Invoke-Kubectl get ingress -n velo
Invoke-Kubectl get 'pods,jobs,deployments' -n velo
Write-Host 'Local Kubernetes API is ready (Development with simulated OTP).'
