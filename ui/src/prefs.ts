const STORAGE_KEY = 'pcmonitor-prefs';

/** Assumptions behind the power estimate; only CPU/GPU package power is actually measured. */
export interface PowerAssumptions {
  monitors: number;
  monitorWatts: number;
  usbWatts: number;
  platformWatts: number;
  efficiencyPercent: number;
}

export interface Prefs {
  showRaw: boolean;
  hiddenFans: string[];
  power: PowerAssumptions;
}

export const DEFAULT_POWER: PowerAssumptions = {
  monitors: 2,
  monitorWatts: 32,
  usbWatts: 15,
  platformWatts: 65,
  efficiencyPercent: 88
};

function number(value: unknown, fallback: number, min: number, max: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
}

export function sanitize(raw: unknown): Prefs {
  const input = (typeof raw === 'object' && raw !== null ? raw : {}) as Record<string, unknown>;
  const power = (typeof input['power'] === 'object' && input['power'] !== null ? input['power'] : {}) as Record<string, unknown>;
  const hidden = Array.isArray(input['hiddenFans']) ? input['hiddenFans'].filter((v): v is string => typeof v === 'string') : [];

  return {
    showRaw: typeof input['showRaw'] === 'boolean' ? input['showRaw'] : true,
    hiddenFans: hidden,
    power: {
      monitors: number(power['monitors'], DEFAULT_POWER.monitors, 0, 12),
      monitorWatts: number(power['monitorWatts'], DEFAULT_POWER.monitorWatts, 0, 500),
      usbWatts: number(power['usbWatts'], DEFAULT_POWER.usbWatts, 0, 500),
      platformWatts: number(power['platformWatts'], DEFAULT_POWER.platformWatts, 0, 500),
      efficiencyPercent: number(power['efficiencyPercent'], DEFAULT_POWER.efficiencyPercent, 50, 100)
    }
  };
}

function load(): Prefs {
  try {
    return sanitize(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}'));
  } catch {
    return sanitize({});
  }
}

let current = load();
const listeners = new Set<() => void>();

export function prefs(): Prefs {
  return current;
}

export function updatePrefs(change: (draft: Prefs) => void): void {
  const draft = structuredClone(current);
  change(draft);
  current = sanitize(draft);
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(current));
  } catch {
    // Storage can be unavailable; preferences then last for this session only.
  }
  listeners.forEach(listener => listener());
}

export function onPrefsChange(listener: () => void): void {
  listeners.add(listener);
}
