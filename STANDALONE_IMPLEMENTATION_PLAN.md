# Standalone PC Monitor Implementation Plan

## Objective

Make `LibreHWDashboard.exe` a true standalone Windows monitoring application.

The app should run without:

- manually starting `LibreHardwareMonitor.exe`
- requiring LibreHardwareMonitor's web server
- requiring Node.js
- opening a browser tab

## Implemented Architecture

```text
LibreHWDashboard.exe
  ├─ EmbeddedTelemetryCollector
  │  └─ LibreHardwareMonitorLib 0.9.6
  ├─ DashboardServer
  │  ├─ GET / -> index.html
  │  └─ GET /data.json -> embedded telemetry JSON
  ├─ WebView2 native dashboard window
  └─ WinForms tray application context
```

## Completed

- Added `LibreHardwareMonitorLib` dependency.
- Replaced Node runtime dependency in standalone mode.
- Replaced external `LibreHardwareMonitor.exe` startup/supervision with embedded sensor collection.
- Added native .NET HTTP server bound to `127.0.0.1`.
- Added embedded `/data.json` endpoint compatible with the existing frontend shape.
- Preserved optional external telemetry fallback through:
  - `PCMONITOR_TELEMETRY_SOURCE`
  - `LIBREHW_SOURCE`
- Kept native WebView2 window and tray behavior.
- Added `Reset telemetry` tray action to recreate the embedded collector.
- Added application manifest with `requireAdministrator` so embedded telemetry can access CPU SMU, motherboard fan and low-level hardware sensors.
- Validated standalone operation with:
  - `LibreHardwareMonitorProcesses = 0`
  - managed `server.js` Node processes = `0`
  - `/data.json` response header `X-PCMonitor-Telemetry: embedded`

## Runtime Defaults

| Setting | Default |
|---|---:|
| Dashboard bind address | `127.0.0.1` |
| Dashboard port | `8096` |
| Telemetry mode | `embedded` |
| External telemetry source | unset |
| UI host | WebView2 |
| Tray mode | enabled |
| Privilege level | Administrator |

## External Fallback

External LibreHardwareMonitor-compatible telemetry remains possible:

```powershell
$env:PCMONITOR_TELEMETRY_SOURCE = "http://127.0.0.1:8085/data.json"
.\LibreHWDashboard.exe
```

This is intended for debugging, comparison with official LibreHardwareMonitor output, or remote telemetry scenarios.

## Remaining Hardening Before Release

1. Add persisted settings file under `%APPDATA%\LibreHWStats\settings.json`.
2. Add settings UI for:
   - dashboard port
   - refresh interval
   - optional external telemetry source
   - start minimized
3. Add explicit startup diagnostics screen for collector errors.
4. Compare embedded JSON against official LibreHardwareMonitor `/data.json` on the target machine.
5. Validate sensor coverage for:
   - CPU temperature/power/clocks
   - GPU thermals, fans, VRAM and clocks
   - NVMe temperature and usage
   - network throughput
6. Package release files:
   - `LibreHWDashboard.exe`
   - `index.html`
   - `WebView2Loader.dll`
   - `MonoPosixHelper.dll`
   - `libMonoPosixHelper.dll`
   - `runtimes\`
7. Document Administrator-mode limitations for hardware requiring privileged sensor access.

## Acceptance Criteria

- Starting only `LibreHWDashboard.exe` shows the dashboard.
- Windows requests Administrator approval at startup.
- `LibreHardwareMonitor.exe` is not started.
- Node.js is not started.
- `/data.json` returns embedded telemetry.
- UI renders existing dashboard sections using embedded telemetry.
- External telemetry remains available as an opt-in fallback.
