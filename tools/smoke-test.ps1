#Requires -Version 7.0
<#
.SYNOPSIS
Starts PCMonitor.exe, waits for telemetry and asserts the standalone acceptance criteria.
.EXAMPLE
.\smoke-test.ps1 -ExePath .\publish\PCMonitor.exe
#>
[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\publish\PCMonitor.exe'),
    [int]$Port = 8199,
    [int]$TimeoutSeconds = 90,
    [switch]$AllowNonElevated
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
$process = $null

function Assert-That([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "PASS  $Message" -ForegroundColor Green }
    else { Write-Host "FAIL  $Message" -ForegroundColor Red; $failures.Add($Message) }
}

try {
    if (-not (Test-Path $ExePath)) { throw "Executable not found: $ExePath" }
    $base = "http://127.0.0.1:$Port"
    $env:PORT = "$Port"
    if ($AllowNonElevated) { $env:__COMPAT_LAYER = 'RunAsInvoker' }
    $process = Start-Process -FilePath $ExePath -ArgumentList '--minimized' -PassThru

    $health = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod "$base/api/health" -TimeoutSec 3
            if ($health.status -in 'ok', 'degraded', 'error') { break }
        } catch { }
        Start-Sleep -Seconds 2
    }

    Assert-That ($null -ne $health) 'health endpoint reachable'
    Assert-That ($health.sensorCount -gt 0) "sensors reported ($($health.sensorCount))"
    Assert-That ($health.mode -eq 'embedded') 'telemetry mode is embedded'
    if (-not $AllowNonElevated) { Assert-That ([bool]$health.elevated) 'process is elevated' }

    $data = Invoke-WebRequest "$base/data.json" -UseBasicParsing
    Assert-That ([string]$data.Headers['X-PCMonitor-Telemetry'] -eq 'embedded') 'data.json served from embedded collector'
    $page = Invoke-WebRequest "$base/" -UseBasicParsing
    Assert-That ($page.Content -match '<title>PC Monitor</title>') 'embedded index.html served'

    Assert-That ($null -eq (Get-CimInstance Win32_Process -Filter "Name='LibreHardwareMonitor.exe'" | Where-Object { $_.ParentProcessId -eq $process.Id })) 'LibreHardwareMonitor.exe not spawned'
    Assert-That ($null -eq (Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.ParentProcessId -eq $process.Id })) 'node.exe not spawned'

    try {
        $null = Invoke-WebRequest "$base/data.json" -Headers @{ Host = 'evil.example' } -UseBasicParsing
        Assert-That $false 'foreign Host header rejected'
    } catch { Assert-That ($_.Exception.Response.StatusCode -eq 403) 'foreign Host header rejected' }
}
catch {
    Write-Host "ERROR $($_.Exception.Message)" -ForegroundColor Red
    $failures.Add($_.Exception.Message)
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:PORT, Env:__COMPAT_LAYER -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "`n$($failures.Count) check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host "`nAll checks passed" -ForegroundColor Green
