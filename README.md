# PC Monitor

Standalone Windows hardware monitoring dashboard. One self-contained executable: no LibreHardwareMonitor app, no Node.js, no browser, no .NET install.

```text
PCMonitor.exe
  ├─ LibreHardwareMonitorLib   background sampler (one polling loop, immutable snapshot)
  ├─ loopback HTTP server      127.0.0.1:8096  /  /data.json  /api/health  /api/settings
  ├─ embedded dashboard UI     index.html compiled into the exe
  └─ WebView2 window + tray    close-to-tray, fullscreen (F11), autostart toggle
```

## Install

- **Installer:** `PCMonitor-Setup-<version>.exe` from the Releases page (optional autostart task, Start Menu entry).
- **Portable:** `PCMonitor-<version>-portable.zip`, run `PCMonitor.exe`.

Windows asks for Administrator approval at launch: CPU SMU, motherboard fan/voltage and DIMM sensors need ring-0 access. Without elevation the dashboard still runs but reports `degraded` and misses those sensors.

Requires the WebView2 Runtime (included in Windows 11 and current Edge). If it is missing, the app offers to open the dashboard in your default browser instead.

## Usage

- Closing the window hides it to the tray (configurable). Double-click the tray icon to reopen.
- Starting a second instance brings the running one to the front.
- Tray menu: Open, Fullscreen, Reload, Reset telemetry, Start with Windows, Open in browser, Open logs folder, Exit.
- The tray tooltip shows live CPU load and temperature.
- Sidebar shows telemetry health (sensor count, elevation, errors) and a Settings panel.

## Settings

Stored in `%APPDATA%\PCMonitor\settings.json`, editable in the dashboard or by hand.

| Key | Default | Description |
|---|---:|---|
| `Port` | `8096` | Dashboard port. If busy, the next free port (up to +19) is used. Restart to apply. |
| `SamplingMs` | `1000` | Hardware polling interval (250-60000). |
| `StartMinimized` | `false` | Start hidden in the tray. |
| `CloseToTray` | `true` | Close button hides instead of exiting. |
| `ExternalSource` | `null` | Proxy an external LibreHardwareMonitor `/data.json` instead of embedded telemetry. |
| `EnableCpu/Gpu/Memory/Motherboard/Storage/Network/Battery/Psu/Controller` | `true` | Toggle hardware groups (restart collector via tray: Reset telemetry). |

Environment overrides (this run only): `PORT`, `PCMONITOR_TELEMETRY_SOURCE` (alias `LIBREHW_SOURCE`).

**Start with Windows** creates a Scheduled Task (`PCMonitor`, at logon, highest privileges) so the app starts elevated without a UAC prompt.

Logs: `%APPDATA%\PCMonitor\logs\` (7 days). UI override for development: place an `index.html` in `%APPDATA%\PCMonitor\ui\`.

## HTTP API

| Endpoint | Description |
|---|---|
| `GET /data.json` | LibreHardwareMonitor-compatible sensor tree. Adds `ValueNum`, `MinNum`, `MaxNum`, `Unit` numeric fields. |
| `GET /api/health` | `status` (`starting`/`ok`/`degraded`/`error`), `elevated`, `sensorCount`, `hardwareCount`, `lastError`, `lastUpdate`. |
| `GET/PUT /api/settings` | Read or update port, sampling, startMinimized, closeToTray, autostart (same-origin JSON only). |

The server binds to loopback only and rejects requests whose `Host` is not `127.0.0.1:<port>` / `localhost:<port>`.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet publish launcher\LibreHWDashboardLauncher.csproj -c Release -o publish
```

Output: `publish\PCMonitor.exe` (self-contained, single file, win-x64). Installer: `iscc /DAppVersion=2.0.0 installer\PCMonitor.iss`.

Release: push a `v*` tag; GitHub Actions builds the installer, portable ZIP and `SHA256SUMS.txt`.

## Verification tools

- `tools\smoke-test.ps1 -ExePath publish\PCMonitor.exe` - starts the app and checks health, embedded telemetry, no spawned LibreHardwareMonitor/Node, Host-header rejection (`-AllowNonElevated` for non-admin shells).
- `tools\compare-telemetry.ps1 -Reference http://127.0.0.1:8085/data.json -Candidate http://127.0.0.1:8096/data.json` - sensor coverage diff against the official LibreHardwareMonitor web server.

## Known limitations

- Windows 10/11 x64 only.
- LibreHardwareMonitor loads a ring-0 driver; some antivirus/HVCI configurations block it, which shows as `degraded`/missing sensors in the health box. See `%APPDATA%\PCMonitor\logs`.
- Uses the pre-release `LibreHardwareMonitorLib 0.9.7-pre705`: SATA drives are identified as `/ahci/N` instead of `/ssd/N`, and a few NVMe-specific `Level` sensors present in 0.9.6 are not reported.
- First launch after install can take up to ~35 s to open all hardware (one-time native extraction and driver init); later launches take a few seconds.

## License

MIT. See `THIRD_PARTY_NOTICES.md` for bundled components.
