import { clamp } from './format';
import { h, type Safe } from './html';
import { spinSeconds } from './fans';
import { gaugeIcon } from './icons';
import { colorForPercent, isPercentLike, tempColor } from './scoring';
import type { Sensor } from './types';

const EMPTY = h``;

function dial(sensor: Sensor | undefined, label: string, pct: number, color: string): Safe {
  return h`<div class="dial" style="--dial:${clamp(pct, 0, 100)}%;--dial-color:${color}">
    <span class="dial-icon" aria-hidden="true">${gaugeIcon(sensor, label)}</span>
  </div>`;
}

/** Circular gauge for a sensor; `forcedPct` overrides the percentage derived from the value. */
export function signal(sensor: Sensor | undefined, label: string, sub?: string, forcedPct: number | null = null): Safe {
  if (!sensor) return EMPTY;
  const derived = sensor.type === 'Temperature' || isPercentLike(sensor) ? clamp(sensor.number ?? 0, 0, 100) : null;
  const pct = forcedPct ?? derived;
  const colorBasis = sensor.type === 'Temperature' ? sensor.number : pct;
  const color = colorForPercent(colorBasis ?? 0);
  return h`<article class="signal">
    ${dial(sensor, label, pct ?? 0, color)}
    <div class="signal-copy">
      <div class="signal-head">
        <div class="signal-name" title="${sensor.pathText}">${label || sensor.displayName}</div>
        <div class="signal-value">${sensor.valueText || '--'}</div>
      </div>
      <div class="signal-sub">${sub || sensor.deviceName}</div>
    </div>
  </article>`;
}

export function valueSignal(label: string, value: string, sub: string, pct: number, color = 'var(--blue)'): Safe {
  return h`<article class="signal">
    ${dial(undefined, label, pct, color)}
    <div class="signal-copy">
      <div class="signal-head">
        <div class="signal-name">${label}</div>
        <div class="signal-value">${value}</div>
      </div>
      <div class="signal-sub">${sub}</div>
    </div>
  </article>`;
}

export function thermalTile(sensor: Sensor | undefined, label: string, sub?: string): Safe {
  if (!sensor) return EMPTY;
  const color = tempColor(sensor.number);
  return h`<article class="metric-tile" style="border-color:${color}">
    <div class="metric-title"><strong title="${sensor.pathText}">${label || sensor.displayName}</strong></div>
    <div class="metric-value" style="color:${color}">${sensor.valueText || '--'}</div>
    <div class="metric-sub">${sub || sensor.deviceName}</div>
  </article>`;
}

export function unavailableTile(label: string, sub: string, type = 'SENSOR'): Safe {
  return h`<article class="metric-tile" style="border-color:var(--line-soft)">
    <div class="metric-title"><strong>${label}</strong><span class="metric-type">${type}</span></div>
    <div class="metric-value" style="color:var(--muted)">Unavailable</div>
    <div class="metric-sub">${sub}</div>
  </article>`;
}

export function numericTile(sensor: Sensor | undefined, label: string, sub: string | undefined, type = 'VALUE'): Safe {
  if (!sensor) return EMPTY;
  return h`<article class="metric-tile">
    <div class="metric-title">
      <strong title="${sensor.pathText}">${label || sensor.displayName}</strong>
      <span class="metric-type">${type}</span>
    </div>
    <div class="metric-value">${sensor.valueText || '--'}</div>
    <div class="metric-sub">${sub || sensor.deviceName}</div>
  </article>`;
}

/** A thermal tile, or an explanatory placeholder when the CPU temperature is not readable. */
export function cpuThermalTile(sensor: Sensor | undefined): Safe {
  return sensor
    ? thermalTile(sensor, sensor.text, sensor.deviceName)
    : unavailableTile('CPU temperature', 'Check System status for driver access and hardware support', 'TEMP');
}

const BLADE = 'M24 23C22 15 26 8 34 8C33 15 30 21 25 24Z';

function rotor(spin: number | null): Safe {
  const blades = [0, 72, 144, 216, 288].map(angle => h`<path d="${BLADE}" transform="rotate(${angle} 24 24)"/>`);
  return h`<svg viewBox="0 0 48 48" aria-hidden="true">
    <circle class="fan-ring" cx="24" cy="24" r="22"/>
    <g class="fan-rotor${spin === null ? ' is-idle' : ''}" style="--spin:${spin ?? 0}s">
      <g class="fan-blades">${blades}</g>
      <circle class="fan-hub" cx="24" cy="24" r="4.5"/>
    </g>
  </svg>`;
}

/** Fan or pump card; the rotor spins at a speed that follows the reading. */
export function coolingCard(sensor: Sensor, key: string, index: number): Safe {
  const raw = sensor.number ?? 0;
  const load = sensor.type === 'Fan' ? Math.min(100, raw / 30) : clamp(raw, 0, 100);
  const label = sensor.text.replace(/^fan\s*/i, 'Fan ') || `Fan ${index + 1}`;
  return h`<article class="fan-card" data-key="${key}" title="${sensor.pathText}" style="--fan-color:${colorForPercent(load)}">
    <div class="fan-rotor-box">${rotor(spinSeconds(sensor))}</div>
    <div class="fan-copy">
      <div class="fan-name">${label}</div>
      <div class="fan-value">${sensor.valueText || '--'}</div>
      <div class="fan-sub">${sensor.deviceName}</div>
    </div>
  </article>`;
}
