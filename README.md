# PC Monitor

Standalone Windows hardware monitoring dashboard. One self-contained executable: no LibreHardwareMonitor app, no Node.js, no browser, no .NET install.

```text
PCMonitor.exe
  ├─ LibreHardwareMonitorLib   one background polling loop -> immutable snapshots
  ├─ Kestrel (127.0.0.1)       /api/snapshot  /api/stream (SSE)  /api/health  /api/settings
  ├─ embedded dashboard        Vite + TypeScript build compiled into the exe
  └─ WebView2 window + tray    close-to-tray, F11 fullscreen, autostart toggle
```

## Install

- **Installer:** `PCMonitor-Setup-<version>.exe` from the Releases page (optional autostart task, Start Menu entry).
- **Portable:** `PCMonitor-<version>-portable.zip`, run `PCMonitor.exe`.

Windows asks for Administrator approval at launch: CPU SMU, motherboard fan/voltage and DIMM sensors need ring-0 access. Without elevation the dashboard still runs but reports `degraded` and misses those sensors.

Requires the WebView2 Runtime (included in Windows 11 and current Edge). If it is missing, the app offers to open the dashboard in your default browser instead.

## Usage

- Closing the window hides it to the tray (configurable). Double-click the tray icon to reopen.
- Starting a second instance brings the running one to the front.
- Tray menu: Open, Fullscreen, Reload, Reset telemetry, Start with Windows, Open in browser, Open logs folder, Exit. The tooltip shows live CPU load and temperature.
- The top bar shows system risk (hover for the reasons) and telemetry health (sensor count, elevation, errors). The gear opens Settings as a popover; every change is saved automatically and confirmed with a toast. Data is pushed live over SSE.
- Settings > Display: show or hide the raw sensor matrix and individual cooling fans. Settings > Power estimate: tune the assumptions behind the wall-power estimate (only CPU and GPU package power are measured). Display and power settings are stored per browser.
- Fan rotors spin at a speed that follows the RPM reading. The network total counts physical adapters only (VPN tunnels and virtual switches are listed but not summed).

## Settings

Stored in `%APPDATA%\PCMonitor\settings.json`, editable in the dashboard or by hand.

| Key | Default | Description |
|---|---:|---|
| `Port` | `8096` | Dashboard port. If busy, the next free port (up to +19) is used. Restart to apply. |
| `SamplingMs` | `1000` | Hardware polling interval (250-60000), applied live. |
| `StartMinimized` | `false` | Start hidden in the tray. |
| `CloseToTray` | `true` | Close button hides instead of exiting. |
| `EnableCpu/Gpu/Memory/Motherboard/Storage/Network/Battery/Psu/Controller` | `true` | Toggle hardware groups (tray: Reset telemetry to apply). |

The `PORT` environment variable overrides the port for that run only.

**Start with Windows** creates a Scheduled Task (`PCMonitor`, at logon, highest privileges) so the app starts elevated without a UAC prompt.

Logs: `%APPDATA%\PCMonitor\logs\` (7 days). UI override for development: put a built dashboard (`index.html` + `assets/`) in `%APPDATA%\PCMonitor\ui\`.

## LAN access

Off by default. Enable **Allow LAN access** in Settings (or `AllowLan` in `settings.json`) and restart.

- The server then listens on all interfaces, and PC Monitor adds a Windows Firewall rule (Private profile only, program-based).
- Only private-network clients (10.x, 172.16-31.x, 192.168.x, 169.254.x) are served; anything else gets 403.
- Every LAN client needs the access token. Open the link from Settings (or tray: Copy LAN link), e.g. `http://192.168.0.10:8096/?token=...`; the token becomes an HttpOnly cookie and disappears from the address bar.
- LAN viewers get the dashboard only. Settings are available on the PC itself. **New token** invalidates every existing link.
- Traffic is plain HTTP: use it on trusted networks only. The token is stored in `settings.json`.

## HTTP API

| Endpoint | Description |
|---|---|
| `GET /api/snapshot` | Latest snapshot: `timestamp`, `hardware[]`, `sensors[]` (`id`, `device`, `path`, `group`, `name`, `type`, `value`, `min`, `max`, `unit`, `decimals`). Throughput is in bytes per second. |
| `GET /api/stream` | Server-sent events: `snapshot` on every sample, `health` on change, `: ping` heartbeats. |
| `GET /api/health` | `status` (`starting`/`ok`/`degraded`/`error`), `elevated`, `sensorCount`, `hardwareCount`, `lastError`, `lastUpdate`. |
| `GET/PUT /api/settings` | Read or update port, samplingMs, startMinimized, closeToTray, autostart (same-origin JSON only). |

By default the server binds to loopback only and rejects requests whose `Host` is not `127.0.0.1:<port>` / `localhost:<port>` (anti DNS-rebinding). See LAN access above for the optional network mode.

## Development

Requires the .NET 10 SDK and Node 22+.

```powershell
cd ui; npm ci                  # once
npm run dev                    # Vite dev server with /api proxied to a running PCMonitor on :8096
npm test                       # UI unit tests (vitest)
cd ..
dotnet test tests\PCMonitor.Tests          # backend unit + integration tests
dotnet publish src\PCMonitor -c Release -o publish   # builds the UI, embeds it, writes publish\PCMonitor.exe
```

Layout: `src/PCMonitor` (app), `ui` (dashboard source), `tests/PCMonitor.Tests`, `installer` (Inno Setup), `tools` (PowerShell helpers).

- `tools\smoke-test.ps1 -ExePath publish\PCMonitor.exe` starts the app and checks health, snapshot, embedded UI, no spawned LibreHardwareMonitor/Node, Host-header rejection (`-AllowNonElevated` for non-admin shells).
- `tools\compare-telemetry.ps1 -Reference http://127.0.0.1:8085/data.json -Candidate http://127.0.0.1:8096/api/snapshot` diffs sensor coverage against the official LibreHardwareMonitor web server.

Installer: `iscc /DAppVersion=2.0.0 installer\PCMonitor.iss`. Release: push a `v*` tag; GitHub Actions runs the tests, builds the installer, portable ZIP and `SHA256SUMS.txt`.

## Known limitations

- Windows 10/11 x64 only.
- LibreHardwareMonitor loads a ring-0 driver; some antivirus/HVCI configurations block it, which shows as `degraded`/missing sensors in the health box. See the logs folder.
- Uses the pre-release `LibreHardwareMonitorLib 0.9.7-pre705`: SATA drives are identified as `/ahci/N` instead of `/ssd/N`, and a few NVMe-specific `Level` sensors present in 0.9.6 are not reported.
- Opening all hardware can take up to ~35 s on the very first launch (native extraction, driver init); later launches take a few seconds. The dashboard shows `Starting...` meanwhile.

## License

MIT. See `THIRD_PARTY_NOTICES.md` for bundled components.
