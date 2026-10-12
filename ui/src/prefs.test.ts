import { describe, expect, it } from 'vitest';
import { DEFAULT_POWER, sanitize } from './prefs';

describe('sanitize', () => {
  it('returns defaults for garbage input', () => {
    expect(sanitize(null)).toEqual({ showRaw: true, hiddenFans: [], power: DEFAULT_POWER });
    expect(sanitize('nope')).toEqual({ showRaw: true, hiddenFans: [], power: DEFAULT_POWER });
  });

  it('keeps valid values', () => {
    const prefs = sanitize({ showRaw: false, hiddenFans: ['a:#1'], power: { monitors: 3, efficiencyPercent: 92 } });

    expect(prefs.showRaw).toBe(false);
    expect(prefs.hiddenFans).toEqual(['a:#1']);
    expect(prefs.power.monitors).toBe(3);
    expect(prefs.power.efficiencyPercent).toBe(92);
    expect(prefs.power.monitorWatts).toBe(DEFAULT_POWER.monitorWatts);
  });

  it('clamps out-of-range numbers and drops non-string fan keys', () => {
    const prefs = sanitize({ hiddenFans: ['ok', 5, null], power: { monitors: 99, efficiencyPercent: 10, usbWatts: -4 } });

    expect(prefs.hiddenFans).toEqual(['ok']);
    expect(prefs.power.monitors).toBe(12);
    expect(prefs.power.efficiencyPercent).toBe(50);
    expect(prefs.power.usbWatts).toBe(0);
  });

  it('falls back for non-numeric values', () => {
    expect(sanitize({ power: { platformWatts: Number.NaN } }).power.platformWatts).toBe(DEFAULT_POWER.platformWatts);
    expect(sanitize({ power: { monitorWatts: '5' } }).power.monitorWatts).toBe(DEFAULT_POWER.monitorWatts);
  });
});
