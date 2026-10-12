#Requires -Version 7.0
<#
.SYNOPSIS
Compares sensor coverage and values between two LibreHardwareMonitor-compatible /data.json endpoints.
.EXAMPLE
.\compare-telemetry.ps1 -Reference http://127.0.0.1:8085/data.json -Candidate http://127.0.0.1:8096/data.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][string]$Candidate,
    [double]$TolerancePercent = 25
)

$ErrorActionPreference = 'Stop'

function Get-SensorMap {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Url)

    $root = Invoke-RestMethod -Uri $Url -TimeoutSec 15
    $map = @{}
    function Walk($node) {
        if ($node.SensorId) {
            $map[$node.SensorId] = $node
        }
        foreach ($child in @($node.Children)) { Walk $child }
    }
    Walk $root
    return $map
}

function ConvertTo-Number([string]$Text) {
    if ($Text -match '-?\d+(?:[.,]\d+)?') {
        return [double]($Matches[0] -replace ',', '.')
    }
    return $null
}

try {
    $ref = Get-SensorMap -Url $Reference
    $cand = Get-SensorMap -Url $Candidate

    $missing = $ref.Keys | Where-Object { -not $cand.ContainsKey($_) } | Sort-Object
    $extra = $cand.Keys | Where-Object { -not $ref.ContainsKey($_) } | Sort-Object

    $divergent = foreach ($id in ($ref.Keys | Where-Object { $cand.ContainsKey($_) })) {
        $a = ConvertTo-Number $ref[$id].Value
        $b = ConvertTo-Number $cand[$id].Value
        if ($null -eq $a -or $null -eq $b) { continue }
        $base = [Math]::Max([Math]::Abs($a), 1.0)
        $delta = [Math]::Abs($a - $b) / $base * 100
        # Load, throughput and clocks fluctuate between two polls; only flag static-ish sensors.
        if ($delta -gt $TolerancePercent -and $ref[$id].Type -in 'Temperature', 'Voltage', 'Fan', 'Data', 'SmallData') {
            [PSCustomObject]@{ SensorId = $id; Reference = $ref[$id].Value; Candidate = $cand[$id].Value; DeltaPercent = [Math]::Round($delta, 1) }
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

    if ($missing) { Write-Host "`nMissing in candidate:" -ForegroundColor Yellow; $missing | ForEach-Object { "  $_  ($($ref[$_].Value))" } }
    if ($extra) { Write-Host "`nExtra in candidate:" -ForegroundColor Cyan; $extra | ForEach-Object { "  $_" } }
    if ($divergent) { Write-Host "`nDivergent values:" -ForegroundColor Red; $divergent | Format-Table -AutoSize }
}
catch {
    Write-Error "Comparison failed: $($_.Exception.Message)"
    exit 1
}
