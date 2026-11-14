<#
.SYNOPSIS
  Stops everything Riwaq started: the API, the Angular dev server, and the hidden browser used for PDF export.

.DESCRIPTION
  Only processes that belong to this project are stopped (matched by the project path or by Riwaq in their command
  line), so other dotnet or node programs and your normal browser windows are left alone.
#>
$root = $PSScriptRoot

$frontend = Join-Path $root 'frontend'
# Narrow matches only: editor language servers and other tools may also mention the project path.
$project = Get-CimInstance Win32_Process -Filter "Name='node.exe' OR Name='dotnet.exe' OR Name='testhost.exe' OR Name='Riwaq.Api.exe' OR Name='cmd.exe'" |
    Where-Object {
        $proc = $_   # inside switch, $_ is the switch value, not the process
        switch ($proc.Name) {
            'Riwaq.Api.exe' { $true }
            'node.exe'      { $proc.CommandLine -like "*$frontend\node_modules*" }
            'cmd.exe'       { $proc.CommandLine -like '*ng serve --port 4200*' }
            default         { $proc.CommandLine -like '*Riwaq*' }   # dotnet run / Riwaq.Api.dll / Riwaq.Tests
        }
    }
$pdfBrowser = Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe'" |
    Where-Object { $_.CommandLine -like '*riwaq-pdf-browser*' }

$all = @(@($project) + @($pdfBrowser) | Where-Object { $_ -and $_.ProcessId -ne $PID })
foreach ($p in $all) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host "Stopped $($all.Count) process(es)."

Start-Sleep -Seconds 2
foreach ($port in 5080, 4200) {
    $open = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    Write-Host ("  port {0}: {1}" -f $port, $(if ($open) { 'still in use' } else { 'closed' }))
}
