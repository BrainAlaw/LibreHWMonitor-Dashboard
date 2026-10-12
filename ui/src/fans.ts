import type { Model } from './model';
import type { Sensor } from './types';

const STORAGE_KEY = 'pcmonitor-hiddenfans';
const COOLING_PATTERN = /fan|pump|cooler|cooling|aio/i;

function loadHidden(): Set<string> {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : []);
  } catch {
    return new Set();
  }
}

const hidden = loadHidden();

export function fanKey(sensor: Sensor): string {
  const fanNumber = sensor.text.match(/#\d+/)?.[0] ?? sensor.text.toLowerCase();
  return `${sensor.deviceName}:${fanNumber}`;
}

export function hideFan(key: string): void {
  hidden.add(key);
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([...hidden]));
  } catch {
    // Storage can be unavailable; the fan stays hidden for this session only.
  }
}

function isActive(sensor: Sensor): boolean {
  if (sensor.number === null) return false;
  const isIdle = sensor.number === 0 && sensor.maxNumber === 0;
  if (sensor.type === 'Fan') return !isIdle;
  if (sensor.type === 'Control') {
    return COOLING_PATTERN.test(`${sensor.text} ${sensor.displayName} ${sensor.pathText}`) && !isIdle;
  }
  return false;
}

function categoryRank(sensor: Sensor): number {
  return sensor.category === 'cpu' ? 0 : sensor.category === 'gpu' ? 1 : 2;
}

/** Active fans and pumps, de-duplicated per physical fan (RPM preferred over duty cycle), minus hidden ones. */
export function activeCoolingSensors(model: Model): Sensor[] {
  const deduped = new Map<string, Sensor>();
  for (const sensor of model.sensors.filter(isActive)) {
    const key = fanKey(sensor);
    const existing = deduped.get(key);
    if (!existing || (existing.type !== 'Fan' && sensor.type === 'Fan')) deduped.set(key, sensor);
  }

  return [...deduped.values()]
    .filter(sensor => !hidden.has(fanKey(sensor)))
    .sort((a, b) =>
      categoryRank(a) - categoryRank(b) ||
      a.deviceName.localeCompare(b.deviceName, undefined, { numeric: true }) ||
      a.text.localeCompare(b.text, undefined, { numeric: true }));
}
