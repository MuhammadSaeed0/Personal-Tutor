<#
.SYNOPSIS
  Builds and starts Riwaq locally: the API (http://localhost:5080) and the Angular dev server (http://localhost:4200).

.DESCRIPTION
  Both run in the background with their output in backend/api-run.log and frontend/ng-serve.log.
  The script waits until both answer, then opens the app in the default browser. Stop everything with .\stop.ps1.

.PARAMETER NoBrowser
  Do not open the browser when the app is ready.

.PARAMETER SkipBuild
  Start the API from the last build instead of rebuilding it first.
#>
param([switch]$NoBrowser, [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$backend = Join-Path $root 'backend'
$frontend = Join-Path $root 'frontend'

function Test-Url([string]$url) {
    try { (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { $false }
}

function Wait-Url([string]$url, [string]$name, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Url $url) { Write-Host "  $name is ready: $url" -ForegroundColor Green; return $true }
        Start-Sleep -Seconds 3
    }
    Write-Host "  $name did not answer within $seconds s; see its log file." -ForegroundColor Yellow
    return $false
}

if ((Test-Url 'http://localhost:5080/health/ready') -or (Test-Url 'http://localhost:4200/')) {
    Write-Host 'Riwaq is already running (or a port is taken). Run .\stop.ps1 first to restart it.' -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
    Write-Host 'Installing frontend packages (first run)...'
    Push-Location $frontend; try { npm install --no-audit --no-fund | Out-Null } finally { Pop-Location }
}

$modelDir = Join-Path $backend 'src/Riwaq.Api/App_Data/models/multilingual-e5-small'
if (-not (Test-Path (Join-Path $modelDir 'model_qint8_avx512_vnni.onnx'))) {
    Write-Host 'Downloading the semantic search model (first run, about 120 MB)...'
    try { & (Join-Path $root 'scripts/get-model.ps1') }
    catch { Write-Host "  Model download failed ($_); search will use keywords only for now." -ForegroundColor Yellow }
}

if (-not $SkipBuild) {
    Write-Host 'Building the API...'
    dotnet build (Join-Path $backend 'src/Riwaq.Api/Riwaq.Api.csproj') -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'The API build failed.' }
}

Write-Host 'Starting the API and the web app...'
$apiLog = Join-Path $backend 'api-run.log'
$webLog = Join-Path $frontend 'ng-serve.log'
Start-Process -FilePath 'dotnet' -ArgumentList 'run', '--no-build', '--project', 'src/Riwaq.Api', '--launch-profile', 'http' `
    -WorkingDirectory $backend -WindowStyle Hidden -RedirectStandardOutput $apiLog -RedirectStandardError "$apiLog.err"
Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', 'npx ng serve --port 4200' `
    -WorkingDirectory $frontend -WindowStyle Hidden -RedirectStandardOutput $webLog -RedirectStandardError "$webLog.err"

$apiReady = Wait-Url 'http://localhost:5080/health/ready' 'API' 180
$webReady = Wait-Url 'http://localhost:4200/' 'Web app' 420

if ($apiReady -and $webReady) {
    Write-Host 'Riwaq is running. Stop it with .\stop.ps1' -ForegroundColor Green
    if (-not $NoBrowser) { Start-Process 'http://localhost:4200' }
} else {
    exit 1
}
