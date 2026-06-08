# LibreHW Stats

Real-time hardware telemetry dashboard powered by [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). Zero dependencies, single HTML file, dark theme, auto-discovery.

![screenshot](screenshot.png)

## Features

- **CPU** – per-core load, temperature, package power, voltage
- **GPU** – core/memory load, clocks, temperatures, VRAM usage
- **Power estimation** – wall power, internal DC, per-component breakdown
- **Cooling** – all active fans and pumps with live RPM/percentage
- **Network** – aggregated download/upload with peak history chart
- **Thermal overview** – all critical temperatures in one place
- **Raw sensor matrix** – every sensor LibreHW exposes with health scoring
- **Auto-discovery** – scans local network for LibreHardwareMonitor instances
- **No build step** – vanilla JS, inline CSS, zero npm dependencies

## Quick Start

1. **Download** the latest release ZIP
2. **Run** `LibreHWDashboard.exe` (Windows) – it starts both LibreHardwareMonitor and the dashboard server automatically
3. **Open** `http://127.0.0.1:8096` in your browser

Or start manually:

```bash
# Start LibreHardwareMonitor with remote web server on port 8085
LibreHardwareMonitor.exe

# Start the dashboard server
node server.js

# Open http://127.0.0.1:8096
```

## How It Works

```
LibreHardwareMonitor  ──HTTP──►  server.js (Node.js proxy)  ──HTTP──►  Browser (SPA)
    (:8085 /data.json)               (:8096)
```

- **LibreHardwareMonitor** reads sensor data (CPU, GPU, RAM, storage, fans, etc.) and exposes it as JSON via its built-in remote web server
- **server.js** is a lightweight Node.js proxy that discovers LibreHW on the local network, fetches telemetry, and serves the dashboard
- **index.html** is a single-page application that polls the proxy, normalises the data, and renders everything with vanilla JS

No database, no build tools, no configuration files.

## Configuration

| Environment variable | Default | Description |
|---|---|---|
| `PORT` | `8096` | Dashboard server port |
| `LIBREHW_PORT` | `8085` | Telemetry port to scan |
| `LIBREHW_SOURCE` | *(auto)* | Explicit telemetry URL (disables discovery) |

The telemetry port and refresh interval can also be changed from the dashboard header and are saved to `localStorage`.

## Auto-Discovery

When no `LIBREHW_SOURCE` is set, the server scans all private IPv4 subnets (`10.x.x.x`, `172.16-31.x.x`, `192.168.x.x`) for a valid LibreHardwareMonitor `/data.json` endpoint. Found sources are cached for 5 minutes.

## Manual Fan Hiding

Hover over any fan card and click the `×` button to hide it. Hidden fans persist across page reloads (`localStorage`). Run `localStorage.removeItem('librehw-hiddenfans')` in the browser console to reset.

## Building the Launcher

The .NET launcher (`LibreHWDashboard.exe`) is a WinExe that starts the Node.js server and opens the browser. To rebuild:

```bash
cd launcher
dotnet publish -c Release -o ../publish
```

Requires .NET 10 SDK.

## Requirements

- **Node.js** (for the dashboard server)
- **LibreHardwareMonitor** v0.9.6+ (included in the release)
- **Windows** (LibreHardwareMonitor is Windows-only; the dashboard itself works on any OS)
- A browser (Chrome, Firefox, Edge, etc.)

## License

MIT
