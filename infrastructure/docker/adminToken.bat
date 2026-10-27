@echo off
setlocal
rem Resolve paths from this file, even when called from a different directory.
pushd "%~dp0..\.." || exit /b 1

rem Print only the token; keep Docker errors visible and return failures to CMD.
powershell.exe -NoProfile -Command "$result = & docker compose --env-file infrastructure/docker/.env.local -f compose.yaml exec -T api dotnet Velo.Api.dll --issue-development-operations-token b414afaf-4453-4090-8d31-3fdf413355f6; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; try { $token = ($result | ConvertFrom-Json -ErrorAction Stop).accessToken; if ([string]::IsNullOrWhiteSpace($token)) { throw 'No access token was returned.' }; Write-Output $token } catch { Write-Error $_; exit 1 }"
set "tokenExitCode=%ERRORLEVEL%"
popd
exit /b %tokenExitCode%
