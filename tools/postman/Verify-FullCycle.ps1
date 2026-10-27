$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$apiDll = Join-Path $workspace 'src/Velo.Api/bin/Debug/net10.0/Velo.Api.dll'
if (!(Test-Path -LiteralPath $apiDll)) { throw 'Build Velo.Api first.' }
$connectionString = $env:VELO_TEST_SQL_CONNECTION
if (!$connectionString) {
    $localSettings = Get-Content (Join-Path $workspace 'src/Velo.Api/appsettings.Local.json') -Raw | ConvertFrom-Json
    $connectionString = $localSettings.ConnectionStrings.Velo
}
$sql = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
$sql['Initial Catalog'] = 'master'; $sql['Pooling'] = $false
$masterString = $sql.ConnectionString
$database = 'velo_postman_tests_' + [guid]::NewGuid().ToString('N')
if ($database -notmatch '^velo_postman_tests_[a-f0-9]{32}$') { throw 'Unsafe test database name.' }
$master = [System.Data.SqlClient.SqlConnection]::new($masterString)
$apiProcess = $null; $created = $false
$logBase = Join-Path ([System.IO.Path]::GetTempPath()) ('velo-postman-' + [guid]::NewGuid().ToString('N'))
$environmentKeys = @('ConnectionStrings__Velo','Authentication__OtpHashKey','Authentication__SmsProvider','Authentication__ExposeDevelopmentCodes','ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','VELO_RUNNER_BASE_URL','VELO_RUNNER_OPERATIONS_TOKEN')
$savedEnvironment = @{}
foreach ($key in $environmentKeys) { $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
try {
    $master.Open()
    $command = $master.CreateCommand(); $command.CommandText = "CREATE DATABASE [$database]"; [void]$command.ExecuteNonQuery(); $created = $true
    $master.Close()
    $sql['Initial Catalog'] = $database
    $env:ConnectionStrings__Velo = $sql.ConnectionString
    $env:Authentication__OtpHashKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Authentication__SmsProvider = 'Development'; $env:Authentication__ExposeDevelopmentCodes = 'true'
    & dotnet $apiDll --migrate true *> ($logBase + '.migrations')
    if ($LASTEXITCODE -ne 0) { throw 'Test database migration failed.' }
    if (Get-NetTCPConnection -State Listen -LocalPort 15080 -ErrorAction SilentlyContinue) { throw 'Test port 15080 is occupied.' }
    $apiProcess = Start-Process -FilePath dotnet -ArgumentList $apiDll,'--urls','http://127.0.0.1:15080' -WorkingDirectory (Join-Path $workspace 'src/Velo.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput ($logBase + '.out') -RedirectStandardError ($logBase + '.err')
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        if ($apiProcess.HasExited) { throw 'Test API exited.' }
        try { $health = Invoke-WebRequest 'http://127.0.0.1:15080/health/ready' -UseBasicParsing; if ($health.StatusCode -eq 200) { $ready = $true; break } } catch {}
        Start-Sleep -Milliseconds 250
    }
    if (!$ready) { throw 'Test API did not become ready.' }
    $phone = '+2015' + (Get-Random -Minimum 10000000 -Maximum 99999999)
    $challenge = Invoke-RestMethod 'http://127.0.0.1:15080/api/v1/auth/register' -Method Post -ContentType application/json -Body (@{ phoneNumber=$phone; email='operator@example.test'; name='Test operator' } | ConvertTo-Json)
    $verified = Invoke-RestMethod 'http://127.0.0.1:15080/api/v1/auth/otp/verify' -Method Post -ContentType application/json -Body (@{ challengeId=$challenge.challengeId; code=$challenge.developmentCode } | ConvertTo-Json)
    $operator = Invoke-RestMethod 'http://127.0.0.1:15080/api/v1/auth/me' -Headers @{ Authorization='Bearer ' + $verified.accessToken }
    $output = & dotnet $apiDll --issue-development-operations-token $operator.id 2> ($logBase + '.token-error')
    if ($LASTEXITCODE -ne 0) { throw 'Could not issue isolated test operations access.' }
    $token = $output | Where-Object { $_ -like '{*' } | Select-Object -Last 1 | ConvertFrom-Json
    $env:VELO_RUNNER_BASE_URL = 'http://127.0.0.1:15080'; $env:VELO_RUNNER_OPERATIONS_TOKEN = $token.accessToken
    & node (Join-Path $PSScriptRoot 'verify-full-cycle.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'FullCycle HTTP verification failed.' }
} finally {
    if ($apiProcess -and !$apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id -Force }
    if ($created -and $database -match '^velo_postman_tests_[a-f0-9]{32}$') {
        if ($master.State -ne 'Open') { $master.Open() }
        $command = $master.CreateCommand(); $command.CommandText = "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]"
        [void]$command.ExecuteNonQuery()
    }
    $master.Dispose()
    foreach ($key in $environmentKeys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
    foreach ($suffix in @('.migrations','.out','.err','.token-error')) { Remove-Item -LiteralPath ($logBase + $suffix) -ErrorAction SilentlyContinue }
}
