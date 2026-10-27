#requires -Version 7.0
[CmdletBinding()]
param([ValidateRange(1, 65535)][int]$HttpsPort = 443)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$localDirectory = Join-Path $repoRoot '.local'
$certificateDirectory = Join-Path $localDirectory 'certificates'
$certificatePath = Join-Path $certificateDirectory 'velo.internal.crt'
$keyPath = Join-Path $certificateDirectory 'velo.internal.key'

function Invoke-Kubectl {
    $output = $input | & kubectl --context docker-desktop @args
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." }
    $output
}

New-Item -ItemType Directory -Force $certificateDirectory | Out-Null
if ((Test-Path $certificatePath) -xor (Test-Path $keyPath)) {
    throw 'Incomplete TLS certificate/key pair. Restore the missing file before retrying.'
}
if (-not (Test-Path $certificatePath)) {
    $rsa = [System.Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=*.velo.internal', $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddDnsName('*.velo.internal')
        # Reserve the apex for the future website without adding an ingress now.
        $san.AddDnsName('velo.internal')
        $request.CertificateExtensions.Add($san.Build())
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
                [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment, $true))
        $usages = [System.Security.Cryptography.OidCollection]::new()
        $null = $usages.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $false))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(1))
        try {
            [IO.File]::WriteAllText($certificatePath, $certificate.ExportCertificatePem())
            [IO.File]::WriteAllText($keyPath, $rsa.ExportPkcs8PrivateKeyPem())
        }
        finally { $certificate.Dispose() }
    }
    finally { $rsa.Dispose() }
}

Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s/namespace.yaml')
Invoke-Kubectl create secret tls velo-wildcard-tls -n velo --cert=$certificatePath --key=$keyPath --dry-run=client -o yaml |
    Invoke-Kubectl apply -f -

# Isolate Helm's repository cache from unrelated existing releases.
$helmArguments = @(
    '--kube-context', 'docker-desktop',
    '--repository-config', (Join-Path $localDirectory 'helm/repositories.yaml'),
    '--repository-cache', (Join-Path $localDirectory 'helm/cache'))
& helm upgrade --install velo-ingress traefik --repo https://traefik.github.io/charts --version 42.1.3 `
    --namespace velo-ingress --create-namespace --skip-crds `
    --values (Join-Path $repoRoot 'infrastructure/k8s-local/traefik-values.yaml') `
    --set "ports.websecure.exposedPort=$HttpsPort" --wait --timeout 180s @helmArguments
if ($LASTEXITCODE -ne 0) { throw 'Traefik ingress installation failed.' }

Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s-local/api-ingress.yaml')
Invoke-Kubectl apply -f (Join-Path $repoRoot 'infrastructure/k8s-local/pwa-ingress.yaml')
Write-Host "Ingresses installed on HTTPS port $HttpsPort. Public certificate: $certificatePath"
Write-Host 'Trust this public certificate locally if browser/PWA trust is required; never distribute the private key.'
