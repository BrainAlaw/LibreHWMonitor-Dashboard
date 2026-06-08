const http = require('node:http');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const dashboardPort = Number(process.env.PORT || 8096);
const configuredUpstream = process.env.LIBREHW_SOURCE || '';
const defaultTelemetryPort = Number(process.env.LIBREHW_PORT || 8085);
const root = __dirname;

const DISCOVERY_TIMEOUT_MS = 450;
const DISCOVERY_CONCURRENCY = 48;
const DISCOVERY_CACHE_MS = 5 * 60 * 1000;

const discoveryCache = new Map();

const mimeTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.ico', 'image/x-icon']
]);

function send(response, statusCode, headers, body) {
  response.writeHead(statusCode, {
    'Cache-Control': 'no-store',
    ...headers
  });
  response.end(body);
}

function serveFile(response, requestPath) {
  const requestedFile = requestPath === '/' ? 'index.html' : requestPath.replace(/^\/+/, '');
  const safePath = path.normalize(requestedFile).replace(/^(\.\.[/\\])+/, '');
  const filePath = path.join(root, safePath);

  if (!filePath.startsWith(root)) {
    send(response, 403, { 'Content-Type': 'text/plain; charset=utf-8' }, 'Forbidden');
    return;
  }

  fs.readFile(filePath, (error, content) => {
    if (error) {
      send(response, 404, { 'Content-Type': 'text/plain; charset=utf-8' }, 'Not found');
      return;
    }

    const contentType = mimeTypes.get(path.extname(filePath).toLowerCase()) || 'application/octet-stream';
    send(response, 200, { 'Content-Type': contentType }, content);
  });
}

function validPort(value) {
  const port = Number(value || defaultTelemetryPort);
  if (!Number.isInteger(port) || port < 1 || port > 65535) return defaultTelemetryPort;
  return port;
}

function fetchText(source, timeoutMs = 3000) {
  return new Promise((resolve, reject) => {
    const request = http.get(source, upstreamResponse => {
      let body = '';
      upstreamResponse.setEncoding('utf8');
      upstreamResponse.on('data', chunk => {
        body += chunk;
      });
      upstreamResponse.on('end', () => {
        resolve({
          body,
          statusCode: upstreamResponse.statusCode || 502
        });
      });
    });

    request.setTimeout(timeoutMs, () => {
      request.destroy(new Error(`Timeout after ${timeoutMs} ms`));
    });

    request.on('error', reject);
  });
}

function isLibreHardwarePayload(body) {
  try {
    const json = JSON.parse(body);
    return Boolean(json && typeof json === 'object' && Array.isArray(json.Children));
  } catch {
    return false;
  }
}

function ipv4ToInt(address) {
  return address.split('.').reduce((accumulator, part) => ((accumulator << 8) + Number(part)) >>> 0, 0);
}

function intToIpv4(value) {
  return [
    (value >>> 24) & 255,
    (value >>> 16) & 255,
    (value >>> 8) & 255,
    value & 255
  ].join('.');
}

function isPrivateIpv4(address) {
  const parts = address.split('.').map(Number);
  if (parts.length !== 4 || parts.some(part => !Number.isInteger(part) || part < 0 || part > 255)) return false;
  return parts[0] === 10 ||
    (parts[0] === 172 && parts[1] >= 16 && parts[1] <= 31) ||
    (parts[0] === 192 && parts[1] === 168) ||
    (parts[0] === 169 && parts[1] === 254);
}

function discoveryCandidates(port) {
  const hosts = new Set(['127.0.0.1', 'localhost']);

  Object.values(os.networkInterfaces()).flat().forEach(details => {
    if (!details || details.family !== 'IPv4' || !isPrivateIpv4(details.address)) return;

    const addressInt = ipv4ToInt(details.address);
    const subnetBase = addressInt & 0xffffff00;

    hosts.add(details.address);
    hosts.add(intToIpv4(subnetBase + 1));

    for (let host = 1; host <= 254; host += 1) {
      hosts.add(intToIpv4(subnetBase + host));
    }
  });

  return [...hosts].map(host => `http://${host}:${port}/data.json`);
}

async function discoverTelemetry(port) {
  const cached = discoveryCache.get(port);
  if (cached && cached.expiresAt > Date.now()) return cached.source;

  const candidates = discoveryCandidates(port);
  let index = 0;
  let found = null;

  async function worker() {
    while (!found && index < candidates.length) {
      const source = candidates[index];
      index += 1;

      try {
        const result = await fetchText(source, DISCOVERY_TIMEOUT_MS);
        if (result.statusCode === 200 && isLibreHardwarePayload(result.body)) {
          found = source;
          break;
        }
      } catch {
        // Discovery intentionally ignores connection failures and timeouts.
      }
    }
  }

  await Promise.all(Array.from({ length: Math.min(DISCOVERY_CONCURRENCY, candidates.length) }, worker));

  if (!found) {
    throw new Error(`LibreHW telemetry was not discovered on local private IPv4 networks at port ${port}`);
  }

  discoveryCache.set(port, {
    source: found,
    expiresAt: Date.now() + DISCOVERY_CACHE_MS
  });
  console.log(`Discovered telemetry source: ${found}`);
  return found;
}

async function proxyTelemetry(response, telemetryPort) {
  let preferredSource = configuredUpstream;
  try {
    preferredSource = preferredSource || await discoverTelemetry(telemetryPort);
    const result = await fetchText(preferredSource);
    if (result.statusCode !== 200 || !isLibreHardwarePayload(result.body)) {
      discoveryCache.delete(telemetryPort);
      throw new Error(`Invalid telemetry response from ${preferredSource}`);
    }

    send(response, 200, {
      'Content-Type': 'application/json; charset=utf-8',
      'X-LibreHW-Source': preferredSource
    }, result.body);
  } catch (error) {
    if (!configuredUpstream) discoveryCache.delete(telemetryPort);
    send(response, 502, { 'Content-Type': 'application/json; charset=utf-8' }, JSON.stringify({
      error: 'Failed to fetch LibreHW telemetry',
      detail: error.message,
      source: preferredSource
    }));
  }
}

http.createServer((request, response) => {
  const url = new URL(request.url, `http://${request.headers.host}`);

  if (url.pathname === '/data.json') {
    proxyTelemetry(response, validPort(url.searchParams.get('port')));
    return;
  }

  serveFile(response, decodeURIComponent(url.pathname));
}).listen(dashboardPort, '127.0.0.1', () => {
  console.log(`PC Monitor: http://127.0.0.1:${dashboardPort}/`);
  console.log(configuredUpstream
    ? `Telemetry source: ${configuredUpstream}`
    : `Telemetry discovery enabled on port ${defaultTelemetryPort}`);
});
