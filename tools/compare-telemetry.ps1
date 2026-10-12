#Requires -Version 7.0
<#
.SYNOPSIS
Compares sensor coverage and values between the official LibreHardwareMonitor web server and PC Monitor.
.EXAMPLE
.\compare-telemetry.ps1 -Reference http://127.0.0.1:8085/data.json -Candidate http://127.0.0.1:8096/api/snapshot
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][string]$Candidate,
    [double]$TolerancePercent = 25
)

$ErrorActionPreference = 'Stop'

# Sensor types whose value is static enough to compare between two polls.
$comparableTypes = 'Temperature', 'Voltage', 'Fan', 'Data', 'SmallData'

function Get-ReferenceMap {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Url)

    $map = @{}
    function Walk($node) {
        if ($node.SensorId) {
            $map[$node.SensorId] = [PSCustomObject]@{ Type = $node.Type; Value = ConvertTo-Number $node.Value; Text = $node.Value }
        }
        foreach ($child in @($node.Children)) { Walk $child }
    }
    Walk (Invoke-RestMethod -Uri $Url -TimeoutSec 15)
    return $map
}

function Get-CandidateMap {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Url)

    $map = @{}
    foreach ($sensor in (Invoke-RestMethod -Uri $Url -TimeoutSec 15).sensors) {
        $map[$sensor.id] = [PSCustomObject]@{ Type = $sensor.type; Value = $sensor.value; Text = "$($sensor.value) $($sensor.unit)" }
    }
    return $map
}

function ConvertTo-Number([string]$Text) {
    if ($Text -match '-?\d+(?:[.,]\d+)?') {
        return [double]($Matches[0] -replace ',', '.')
    }
    return $null
}

try {
    $ref = Get-ReferenceMap -Url $Reference
    $cand = Get-CandidateMap -Url $Candidate

    $missing = $ref.Keys | Where-Object { -not $cand.ContainsKey($_) } | Sort-Object
    $extra = $cand.Keys | Where-Object { -not $ref.ContainsKey($_) } | Sort-Object

    $divergent = foreach ($id in ($ref.Keys | Where-Object { $cand.ContainsKey($_) })) {
        $a = $ref[$id].Value
        $b = $cand[$id].Value
        if ($null -eq $a -or $null -eq $b -or $ref[$id].Type -notin $comparableTypes) { continue }
        $delta = [Math]::Abs($a - $b) / [Math]::Max([Math]::Abs($a), 1.0) * 100
        if ($delta -gt $TolerancePercent) {
            [PSCustomObject]@{ SensorId = $id; Reference = $ref[$id].Text; Candidate = $cand[$id].Text; DeltaPercent = [Math]::Round($delta, 1) }
        }
    }

    [PSCustomObject]@{
        ReferenceSensors = $ref.Count
        CandidateSensors = $cand.Count
        Matching         = $ref.Count - @($missing).Count
        Missing          = @($missing).Count
        Extra            = @($extra).Count
        Divergent        = @($divergent).Count
    } | Format-List

    if ($missing) { Write-Host "`nMissing in candidate:" -ForegroundColor Yellow; $missing | ForEach-Object { "  $_  ($($ref[$_].Text))" } }
    if ($extra) { Write-Host "`nExtra in candidate:" -ForegroundColor Cyan; $extra | ForEach-Object { "  $_" } }
    if ($divergent) { Write-Host "`nDivergent values:" -ForegroundColor Red; $divergent | Format-Table -AutoSize }
}
catch {
    Write-Error "Comparison failed: $($_.Exception.Message)"
    exit 1
}
