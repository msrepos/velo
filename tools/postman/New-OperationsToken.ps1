[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Guid] $UserId,
    [Uri] $BaseUrl = 'http://localhost:5080',
    [switch] $CopyToClipboard
)

$ErrorActionPreference = 'Stop'
if (!$BaseUrl.IsLoopback -or $BaseUrl.Scheme -notin @('http', 'https') -or
    $BaseUrl.UserInfo -or $BaseUrl.Query -or $BaseUrl.Fragment -or $BaseUrl.AbsolutePath -ne '/') {
    throw 'BaseUrl must be a local loopback HTTP origin, for example http://localhost:5080.'
}
if ($CopyToClipboard -and !(Get-Command Set-Clipboard -ErrorAction SilentlyContinue)) {
    throw 'Set-Clipboard is unavailable in this PowerShell session.'
}

$project = Join-Path $PSScriptRoot '../../src/Velo.Api'
# Capture output in memory: never write bearer credentials to a log or file.
$output = & dotnet run --project $project --no-build --launch-profile http -- --issue-development-operations-token $UserId.ToString('D') 2>&1
if ($LASTEXITCODE -ne 0) {
    throw 'Token issuance failed. Check the API build, local SQL configuration and verified user ID.'
}
$line = $output | ForEach-Object { "$_" } | Where-Object { $_.StartsWith('{"accessToken":') } | Select-Object -Last 1
if (!$line) { throw 'The command did not return a development access token.' }
$issued = $line | ConvertFrom-Json
$token = [string] $issued.accessToken
if (!$token -or $token -match '\s') { throw 'The issued token is missing or malformed.' }
$headers = @{ Authorization = 'Bearer ' + $token }

foreach ($path in @('/api/v1/auth/me', '/api/v1/admin/inventory/warehouses')) {
    try {
        $response = Invoke-WebRequest -Uri ($BaseUrl.GetLeftPart([UriPartial]::Authority) + $path) -Headers $headers -UseBasicParsing -TimeoutSec 15
    }
    catch {
        $status = if ($_.Exception.Response) { [int] $_.Exception.Response.StatusCode } else { 'connection failed' }
        throw "Verification failed: $path ($status). Start the http API profile and check that the CLI and API use the same database."
    }
    if ($path -eq '/api/v1/auth/me' -and [Guid](($response.Content | ConvertFrom-Json).id) -ne $UserId) {
        throw 'The API returned an unexpected user.'
    }
    Write-Host "$path HTTP $([int]$response.StatusCode)"
}

if ($CopyToClipboard) {
    Set-Clipboard -Value $token
    Write-Host 'Verified operations token copied. Paste directly into Postman Bearer Token, or the operationsAccessToken collection variable.'
} else {
    Write-Host 'Verification passed. Run with -CopyToClipboard to copy the token; credentials are never printed.'
}
Write-Host "Lifetime at issuance: $($issued.expiresIn) seconds."
