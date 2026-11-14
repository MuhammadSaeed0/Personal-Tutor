<#
.SYNOPSIS
  Downloads the semantic search model (multilingual-e5-small, int8 ONNX, about 120 MB) once.

.DESCRIPTION
  The files go to backend/src/Riwaq.Api/App_Data/models/multilingual-e5-small (ignored by git) and are checked
  against their published SHA-256. Without them the app still works, with keyword-based search only.
#>
param([string]$Destination = (Join-Path $PSScriptRoot '..\backend\src\Riwaq.Api\App_Data\models\multilingual-e5-small'))

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes Invoke-WebRequest very slow on Windows PowerShell
$base = 'https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/onnx'
$files = [ordered]@{
    'model_qint8_avx512_vnni.onnx' = 'dd476dd0c2514e9b'
    'tokenizer.json'               = '0b44a9d7b51c3c62'
}

New-Item -ItemType Directory -Force $Destination | Out-Null
foreach ($name in $files.Keys) {
    $target = Join-Path $Destination $name
    $expected = $files[$name]
    if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash.ToLower().StartsWith($expected)) {
        Write-Host "  $name is already present."
        continue
    }
    Write-Host "  Downloading $name ..."
    $part = "$target.part"
    Invoke-WebRequest -Uri "$base/$name" -OutFile $part -UseBasicParsing
    $hash = (Get-FileHash $part -Algorithm SHA256).Hash.ToLower()
    if (-not $hash.StartsWith($expected)) { Remove-Item $part; throw "$name failed its checksum ($hash)." }
    Move-Item -Force $part $target
}
Write-Host 'Semantic search model ready.' -ForegroundColor Green
