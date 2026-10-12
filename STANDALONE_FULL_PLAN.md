# PC Monitor — Full Standalone Plan

Supersedes the "Remaining Hardening" section of `STANDALONE_IMPLEMENTATION_PLAN.md`.

## 1. Audit (current state)

| Area | State | Problem |
|---|---|---|
| `launcher/Program.cs` | Embedded `LibreHardwareMonitorLib` collector + `HttpListener` + WebView2 + tray | Single 800-line file; port/env config only; no logging; no single-instance guard; default `SystemIcons.Application` tray icon; `Computer.Open()` failure is unhandled (surfaces as HTTP 500 only) |
| Telemetry format | Re-creates LHM tree with **pre-formatted strings** (`"45.0 °C"`) | `index.html` has to parse strings back to numbers; locale/unit fragile; wasteful |
| `index.html` (2144 lines) | Reads `/data.json`, port in `localStorage` | Not embedded in exe; must sit next to it; old "telemetry port" UI is a leftover of the proxy era |
| `server.js` | Node proxy with LAN subnet discovery | Obsolete in standalone mode; keep out of the release |
| `install-service.ps1` | NSSM + Node + `server.js` | **Stale / wrong**: starts Node, not the exe. A Windows service also cannot own a tray/WebView2 UI (session 0) |
| `tools/` (12 MB) | Full LibreHardwareMonitor 0.9.6 binaries committed | Not needed by the app; bloats repo |
| Repo hygiene | `LibreHWDashboard.exe`, `launcher/publish/*`, `LibreHW-Stats-v1.0.zip`, native DLLs committed | Binaries belong in releases, not git |
| `release.yml` | Builds exe, zips `index.html`, `server.js`, `tools/` | Ships old layout; no runtime-specific flags; deprecated `create-release`/`upload-release-asset` actions |
| Runtime deps | .NET 10 runtime (`--self-contained false`) + WebView2 Runtime | Fresh machine without .NET 10 cannot start the app |
| Privileges | `requireAdministrator` | Required for ring0 sensors; autostart with admin needs a Scheduled Task (Run key triggers UAC) |

## 2. Target architecture

```text
PCMonitor.exe  (single file, self-contained, win-x64)
  ├─ Core/Telemetry     Collector -> typed snapshot (numeric values + units)
  ├─ Core/Server        HttpListener 127.0.0.1: /  /api/snapshot  /api/health  /api/settings
  ├─ Core/Settings      %APPDATA%\PCMonitor\settings.json
  ├─ Core/Logging       %APPDATA%\PCMonitor\logs\app-.log (rolling)
  ├─ UI/Shell           WinForms host: WebView2 window, tray, fullscreen, autostart toggle
  └─ Embedded resources index.html (+ assets) served from the assembly
```

Decisions:
1. **Self-contained, single file, `win-x64`** (`PublishSingleFile`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`). Removes the .NET 10 install requirement.
2. **Embed `index.html` as a resource**, with optional override from `%APPDATA%\PCMonitor\ui\` for UI development.
3. **Drop the string-formatted LHM clone.** Serve `/api/snapshot` with numeric `value/min/max`, `unit`, `type`, hardware path. Keep `/data.json` as a legacy LHM-compatible view for external tools only.
4. **No Windows service.** UI + ring0 sensors need an interactive admin session. Autostart = Scheduled Task, trigger *At log on*, "Run with highest privileges", no UAC prompt at login.
5. **WebView2**: Evergreen runtime check at startup; if missing, show a dialog with the Microsoft bootstrapper link (or fall back to default browser opening the local URL). Optionally bundle the Evergreen bootstrapper in the installer.
6. **Remove Node entirely** from the product path. `server.js` and `install-service.ps1` go to `legacy/` or are deleted.

## 3. Phases

### Phase 0 — Repo cleanup (1 h)
- `git rm --cached` the committed binaries (`*.exe`, `*.dll`, `*.zip`, `launcher/publish/`, `runtimes/`, `tools/LibreHardwareMonitor-v0.9.6/`); extend `.gitignore`.
- Move `server.js` / `install-service.ps1` to `legacy/` or delete; fix README accordingly.
- Commit the pending work first (current `Program.cs`, manifest, `index.html`) so there is a known-good baseline.

### Phase 1 — Verification of the embedded collector (2 h) — blocks everything else
- Run old LHM 0.9.6 (`tools/…`) and the embedded collector side by side on the target machine; diff sensor sets and values (CPU temp/power/clock, per-core, GPU temp/fan/VRAM/clocks, NVMe temp, NIC throughput, mainboard fans/voltages).
- Check that `LibreHardwareMonitorLib 0.9.7-pre705` (pre-release) gives equal or better coverage than 0.9.6; pin to a stable version if not.
- Throwaway script `tools/compare-telemetry.ps1` that outputs missing/extra/divergent sensors.
- Confirm `hardware.Update()` cadence: currently the whole tree is updated on each request (cached 750 ms). Move to a background timer so multiple clients/requests never trigger hardware polling.

### Phase 2 — Core refactor (half a day)
Split `Program.cs` into files: `Telemetry/TelemetryCollector.cs`, `Telemetry/Snapshot.cs`, `Server/DashboardServer.cs`, `Settings/AppSettings.cs`, `Ui/DashboardForm.cs`, `Ui/TrayContext.cs`, `Program.cs`.
- Background sampler (`PeriodicTimer`, interval from settings, default 1000 ms) writing an immutable snapshot; HTTP handlers only read it.
- Typed snapshot with numeric values; `/api/snapshot` plus legacy `/data.json` adapter.
- Collector errors captured into `/api/health` (`status`, `lastError`, `hardwareCount`, `elevated`) instead of silent 500s.
- Hardware toggles in settings (e.g. disable Network/Storage/Battery/PSU/Controller if slow or problematic). Handle `Computer.Open()` failure → degraded mode with a visible banner in UI.
- Mutex-based **single instance**; second launch focuses the existing window.
- Port conflict: try configured port, on failure pick a free port and pass the URL to WebView2 (no hard error).
- `index.html` adapter: switch to `/api/snapshot`, remove the telemetry-port UI and `localStorage` port logic. Fix parsing helpers that currently split value strings.

### Phase 3 — Settings & UX (half a day)
- `settings.json`: port, refresh interval, start minimized, start with Windows, close-to-tray, window bounds/fullscreen, enabled hardware groups, optional external source (kept as advanced option).
- Settings panel inside the dashboard (`/api/settings` GET/PUT) + tray items: *Start with Windows*, *Start minimized*, *Open logs folder*, *Reset telemetry*.
- Real app icon (`.ico`, embedded in exe and tray); tray tooltip with CPU temp / load.
- Diagnostics view: collector state, elevated yes/no, per-hardware status, last error, log tail.
- Security: bind only to loopback, reject requests with non-local `Host` header (DNS-rebinding), no CORS headers, settings PUT requires same-origin.

### Phase 4 — Autostart & elevation (2 h)
- Scheduled Task create/remove from inside the app (`Microsoft.Win32.TaskScheduler` NuGet or `schtasks.exe`) — highest privileges, At log on, current user, no time limit, start only on AC optional.
- Manifest stays `requireAdministrator`; document the UAC prompt on manual launch.
- Graceful non-admin mode (`asInvoker` fallback is not possible with one manifest, so: detect at startup and warn if token is not elevated, e.g. launched via the task with wrong flags).

### Phase 5 — Packaging & release (half a day)
- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`; check if `PublishTrimmed` is safe (WinForms + LHM use reflection — likely **not**, skip trimming).
- Installer: Inno Setup (or WiX) → `PCMonitor-Setup.exe`: install to `%ProgramFiles%\PCMonitor`, Start Menu shortcut, optional autostart task, uninstall removes task + `%APPDATA%` data (opt-in), checks/installs WebView2 runtime.
- Also publish a portable ZIP (exe only).
- Rewrite `.github/workflows/release.yml`: `windows-latest`, `actions/setup-dotnet@v4`, publish, build installer, `softprops/action-gh-release` with `PCMonitor-Setup.exe` + portable zip + SHA256 sums. Remove `server.js`/`tools/` from the artifact.
- Optional: code-signing (SmartScreen and AV false positives are likely for any app that loads a ring0 driver like LHM's `WinRing0`/`PawnIO`; document this in README).

### Phase 6 — Tests & hardening (ongoing)
- Unit tests for snapshot mapping and settings load/migrate (xUnit).
- Smoke test script: start exe, poll `/api/health`, assert sensors > 0, assert no `LibreHardwareMonitor.exe`/`node.exe` processes.
- Soak test: 24 h, watch handle/memory growth in the collector (LHM can leak on repeated `Reset`).
- Manual matrix: Win 10/11, Intel+NVIDIA, AMD+AMD GPU, laptop (battery), no WebView2 installed, port in use, non-admin start.

## 4. Order & effort

| # | Phase | Effort | Depends on |
|---|---|---|---|
| 0 | Cleanup | 1 h | – |
| 1 | Verify collector | 2 h | 0 |
| 2 | Core refactor | 4 h | 1 |
| 3 | Settings & UX | 4 h | 2 |
| 4 | Autostart/elevation | 2 h | 3 |
| 5 | Packaging/release | 4 h | 2 |
| 6 | Tests/hardening | 3 h+ | 2 |

Minimum viable "fully standalone" cut = phases 0, 1, 2 (background sampler + embedded UI + single instance) and 5 (self-contained exe). Phases 3–4 are polish.

## 5. Risks

- **Ring0 driver**: LHM loads a signed kernel driver; some AV/EDR and Windows "vulnerable driver blocklist" / HVCI settings can block it → sensors vanish. Needs a visible diagnostic (Phase 3), not a silent empty dashboard.
- **Pre-release LHM lib** (`0.9.7-pre705`): API/sensor changes; pin and verify (Phase 1).
- **Single-file self-extract**: native DLLs extract to a temp dir on first run; slower cold start, can be flagged by AV.
- **License**: LibreHardwareMonitorLib is MPL-2.0; bundle its license text in the installer and README.
- **No Linux/macOS**: LHM and WebView2 path are Windows-only; this plan is Windows-only by design.
