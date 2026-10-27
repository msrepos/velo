[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5080
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$envPath = Join-Path $repoRoot 'infrastructure/docker/.env.local'
$composePath = Join-Path $repoRoot 'compose.yaml'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Install Docker Desktop and enable Linux containers first.'
}
$containerOs = docker info --format '{{.OSType}}' 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Start Docker Desktop in Linux container mode, then run this script again.'
}
if ($containerOs -ne 'linux') {
    throw 'Switch Docker Desktop to Linux containers, then run this script again.'
}

if (-not (Test-Path -LiteralPath $envPath)) {
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $passwordBytes = New-Object byte[] 24
        $keyBytes = New-Object byte[] 32
        $random.GetBytes($passwordBytes)
        $random.GetBytes($keyBytes)
        $password = 'Velo1!' + [Convert]::ToBase64String($passwordBytes)
        $otpKey = [Convert]::ToBase64String($keyBytes)
        @(
            '# Local development only. Generated credentials; do not commit or share.'
            "MSSQL_SA_PASSWORD=$password"
            "VELO_OTP_HASH_KEY=$otpKey"
        ) | Set-Content -LiteralPath $envPath -Encoding ascii
    }
    finally {
        $random.Dispose()
    }
    Write-Host 'Created ignored local Docker credentials.'
}

# Keep the SQL password and OTP key stable across restarts of the persistent volume.
$previousPort = $env:VELO_HTTP_PORT
$env:VELO_HTTP_PORT = "$Port"
try {
    docker compose --env-file $envPath -f $composePath up --build -d --wait --wait-timeout 240
    if ($LASTEXITCODE -ne 0) { throw 'Docker startup failed. Inspect docker compose logs using the same --env-file and -f options.' }

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$Port/health/ready" -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch { Start-Sleep -Seconds 2 }
    }
    if (-not $ready) { throw 'Containers started, but API readiness failed. Inspect the API and migrator logs.' }
    Write-Host "VELO is ready at http://localhost:$Port (Development with simulated OTP)."
}
finally {
    if ($null -eq $previousPort) {
        Remove-Item Env:VELO_HTTP_PORT -ErrorAction SilentlyContinue
    }
    else {
        $env:VELO_HTTP_PORT = $previousPort
    }
}
