param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$dashboardDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverPath = Join-Path $dashboardDir "server.js"
$nssmPath = Join-Path $dashboardDir "tools\nssm.exe"
$serviceName = "PCMonitor"
$displayName = "PC Monitor Dashboard"
$nssmUrl = "https://nssm.cc/release/nssm-2.24.zip"
$nssmZip = Join-Path $dashboardDir "tools\nssm-2.24.zip"

function Ensure-Nssm {
    if (Test-Path $nssmPath) { return }

    Write-Host "Downloading NSSM (Non-Sucking Service Manager)..." -ForegroundColor Cyan
    if (-not (Test-Path (Split-Path $nssmZip -Parent))) {
        New-Item -ItemType Directory -Path (Split-Path $nssmZip -Parent) -Force | Out-Null
    }

    try {
        Invoke-WebRequest -Uri $nssmUrl -OutFile $nssmZip -UseBasicParsing
        Expand-Archive -Path $nssmZip -DestinationPath (Join-Path $dashboardDir "tools") -Force
        $nssmExe = Get-ChildItem -Path (Join-Path $dashboardDir "tools") -Recurse -Filter "nssm.exe" | Select-Object -First 1
        if ($nssmExe) {
            Copy-Item $nssmExe.FullName $nssmPath -Force
        }
        Remove-Item $nssmZip -Force
        Write-Host "NSSM downloaded and extracted." -ForegroundColor Green
    } catch {
        Write-Host "Failed to download NSSM automatically." -ForegroundColor Yellow
        Write-Host "Please download manually from: $nssmUrl" -ForegroundColor Yellow
        Write-Host "Extract nssm.exe to: $nssmPath" -ForegroundColor Yellow
        exit 1
    }
}

function Install-Service {
    Ensure-Nssm

    Write-Host "Checking for Node.js..." -ForegroundColor Cyan
    $nodePath = (Get-Command node -ErrorAction SilentlyContinue).Source
    if (-not $nodePath) {
        Write-Host "Node.js is not installed or not in PATH." -ForegroundColor Red
        Write-Host "Install Node.js from: https://nodejs.org/" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Installing service '$serviceName'..." -ForegroundColor Cyan

    & $nssmPath install $serviceName "node" $serverPath
    & $nssmPath set $serviceName DisplayName $displayName
    & $nssmPath set $serviceName Description "Serves LibreHardwareMonitor telemetry data as a web dashboard"
    & $nssmPath set $serviceName AppDirectory $dashboardDir
    & $nssmPath set $serviceName AppEnvironmentExtra PORT=8096
    & $nssmPath set $serviceName Start SERVICE_AUTO_START
    & $nssmPath set $serviceName AppStdout (Join-Path $dashboardDir "logs\stdout.log")
    & $nssmPath set $serviceName AppStderr (Join-Path $dashboardDir "logs\stderr.log")
    & $nssmPath set $serviceName AppRotateFiles 1
    & $nssmPath set $serviceName AppRotateBytes 1048576
    & $nssmPath set $serviceName AppNoConsole 1

    Write-Host "Starting service '$serviceName'..." -ForegroundColor Cyan
    & $nssmPath start $serviceName

    Write-Host "Service '$displayName' installed and started." -ForegroundColor Green
    Write-Host "The server will auto-start on boot." -ForegroundColor Green
}

function Uninstall-Service {
    if (-not (Test-Path $nssmPath)) {
        Write-Host "NSSM not found at $nssmPath" -ForegroundColor Red
        Write-Host "Use 'sc.exe delete $serviceName' to remove manually." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Stopping service '$serviceName'..." -ForegroundColor Cyan
    & $nssmPath stop $serviceName
    Start-Sleep -Seconds 2

    Write-Host "Removing service '$serviceName'..." -ForegroundColor Cyan
    & $nssmPath remove $serviceName confirm

    Write-Host "Service uninstalled." -ForegroundColor Green
}

if ($Uninstall) {
    Uninstall-Service
} else {
    Install-Service
}
