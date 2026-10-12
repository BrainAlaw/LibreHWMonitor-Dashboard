# PC Monitor

Standalone Windows hardware monitoring dashboard powered by `LibreHardwareMonitorLib`, WebView2, and a native local .NET server.

## Current Architecture

`LibreHWDashboard.exe` is the normal runtime entrypoint.

```text
LibreHWDashboard.exe
  ├─ embedded LibreHardwareMonitorLib collector
  ├─ native HTTP server on 127.0.0.1:8096
  ├─ /data.json telemetry endpoint
  └─ WebView2 dashboard window + tray menu
```

The app no longer requires:

- `LibreHardwareMonitor.exe` to be started separately
- Node.js
- browser tabs for normal usage
- LibreHardwareMonitor's built-in web server

`server.js` remains in the repository only as a manual/developer fallback.

## Features

- CPU load, per-core load, package power, voltage and temperature
- GPU load, clocks, power, VRAM, fans and thermals
- RAM, storage, network, motherboard and controller sensors
- Aggregated download/upload chart
- Cooling/fan cards
- Power estimation
- Raw sensor matrix
- Native tray app behavior
- WebView2 windowed and fullscreen modes

## Quick Start

Run:

```powershell
.\LibreHWDashboard.exe
```

The app starts its own local server and opens the dashboard in a native window.
Windows will request Administrator approval because low-level CPU, motherboard, fan and SMU sensors require elevated hardware access.

Default URL:

```text
http://127.0.0.1:8096/
```

Default telemetry endpoint:

```text
http://127.0.0.1:8096/data.json
```

## Tray Menu

- `Open` - show the dashboard window
- `Fullscreen` - toggle borderless fullscreen
- `Reload` - reload WebView2
- `Reset telemetry` - recreate the embedded sensor collector
- `Exit` - close the app and local server

Closing the dashboard window hides it to tray instead of exiting.

## Configuration

| Environment variable | Default | Description |
|---|---:|---|
| `PORT` | `8096` | Local dashboard HTTP server port |
| `PCMONITOR_TELEMETRY_SOURCE` | unset | Optional external `/data.json` source instead of embedded telemetry |
| `LIBREHW_SOURCE` | unset | Backward-compatible alias for `PCMONITOR_TELEMETRY_SOURCE` |

When `PCMONITOR_TELEMETRY_SOURCE` or `LIBREHW_SOURCE` is set, `/data.json` proxies that external source. Otherwise, telemetry is collected directly inside `LibreHWDashboard.exe`.

## Build

Requires .NET 10 SDK.

```powershell
dotnet publish launcher\LibreHWDashboardLauncher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o launcher\publish
```

Runtime files required next to `LibreHWDashboard.exe`:

- `index.html`
- `WebView2Loader.dll`
- `MonoPosixHelper.dll`
- `libMonoPosixHelper.dll`
- `runtimes\`

## Manual Browser Fallback

The old Node proxy can still be used for debugging external LibreHardwareMonitor web server payloads:

```powershell
node server.js
```

This is not required for normal standalone usage.

## Notes

- The app requests Administrator privileges so embedded telemetry can access CPU SMU, motherboard fan and low-level hardware sensors.
- WebView2 Runtime must be installed on Windows. Current Microsoft Edge installations usually provide it.
- The app binds only to `127.0.0.1` by default.

## License

MIT
