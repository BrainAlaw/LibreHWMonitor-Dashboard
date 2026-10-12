import type { Model } from './model';
import { prefs } from './prefs';
import type { Sensor } from './types';

const COOLING_PATTERN = /fan|pump|cooler|cooling|aio/i;

export function fanKey(sensor: Sensor): string {
  const fanNumber = sensor.text.match(/#\d+/)?.[0] ?? sensor.text.toLowerCase();
  return `${sensor.deviceName}:${fanNumber}`;
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

/** Every reporting fan or pump, one sensor per physical fan (RPM preferred over duty cycle). */
export function allCoolingSensors(model: Model): Sensor[] {
  const deduped = new Map<string, Sensor>();
  for (const sensor of model.sensors.filter(isActive)) {
    const key = fanKey(sensor);
    const existing = deduped.get(key);
    if (!existing || (existing.type !== 'Fan' && sensor.type === 'Fan')) deduped.set(key, sensor);
  }

  return [...deduped.values()].sort((a, b) =>
    categoryRank(a) - categoryRank(b) ||
    a.deviceName.localeCompare(b.deviceName, undefined, { numeric: true }) ||
    a.text.localeCompare(b.text, undefined, { numeric: true }));
}

/** The fans the user has not hidden in Settings. */
export function visibleCoolingSensors(model: Model): Sensor[] {
  const hidden = new Set(prefs().hiddenFans);
  return allCoolingSensors(model).filter(sensor => !hidden.has(fanKey(sensor)));
}

/**
 * Rotor period in seconds for a fan reading. RPM is mapped to a calm, readable speed (a real fan at
 * 1000 RPM would be a blur) and quantised to 0.1 s so sensor noise does not retrigger the animation.
 */
export function spinSeconds(sensor: Sensor): number | null {
  const value = sensor.number ?? 0;
  if (value <= 0) return null;
  const seconds = sensor.type === 'Fan' ? 2400 / value : 8 - Math.min(100, value) * 0.068;
  return Math.round(Math.min(8, Math.max(0.3, seconds)) * 10) / 10;
}
