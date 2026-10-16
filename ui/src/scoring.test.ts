import { describe, expect, it } from 'vitest';
import { Model } from './model';
import { bestCpuLoad, bestCpuPackagePower, bestCpuTemperature, chooseStorageTemperature, colorForPercent, scoreSensor, systemIssues, systemLevel, tempColor } from './scoring';
import { sensor, snapshot } from './testing';

const model = (...sensors: ReturnType<typeof sensor>[]) => new Model(snapshot(sensors));
const first = (...sensors: ReturnType<typeof sensor>[]) => model(...sensors).sensors[0]!;

describe('colors', () => {
  it('maps thresholds', () => {
    expect(colorForPercent(50)).toBe('var(--green)');
    expect(colorForPercent(80)).toBe('var(--yellow)');
    expect(colorForPercent(95)).toBe('var(--red)');
    expect(tempColor(72)).toBe('var(--yellow)');
    expect(tempColor(null)).toBe('var(--muted-2)');
  });
});

describe('scoreSensor', () => {
  it.each([['Temperature #3', 89.5], ['Temperature #6', 81], ['Temperature #3', 110]] as const)(
    'does not assert overheating for unidentified Super I/O input %s at %s C', (name, value) => {
      const reading = first(sensor({ id: '/lpc/nct6798d/0/temperature/2', name, type: 'Temperature', value }));
      expect(scoreSensor(reading)).toEqual({ score: 100, level: 'ok', reason: 'Unidentified sensor; alerts disabled' });
    });

  it.each(['CPU', 'Motherboard', 'VRM', 'CPU Socket', 'PCH'])(
    'still alerts for identified Super I/O temperature %s', name => {
      const reading = first(sensor({ id: '/lpc/nct6798d/0/temperature/0', name, type: 'Temperature', value: 95 }));
      expect(scoreSensor(reading).level).toBe('critical');
    });

  it.each(['/nvme/0/temperature/0', '/amdcpu/0/temperature/0', '/gpu-nvidia/0/temperature/0'])(
    'keeps numbered temperature alerts for %s', id => {
      expect(scoreSensor(first(sensor({ id, name: 'Temperature #3', type: 'Temperature', value: 95 }))).level).toBe('critical');
    });

  it('flags hot GPU hot spots later than other temperatures', () => {
    const hotspot = first(sensor({ id: '/gpu-nvidia/0/temperature/1', name: 'GPU Hot Spot', type: 'Temperature', value: 88 }));
    const core = first(sensor({ id: '/gpu-nvidia/0/temperature/0', name: 'GPU Core', type: 'Temperature', value: 88 }));
    expect(scoreSensor(hotspot).level).toBe('warn');
    expect(scoreSensor(core).level).toBe('warn');
    expect(scoreSensor(first(sensor({ id: '/gpu-nvidia/0/temperature/1', name: 'GPU Hot Spot', type: 'Temperature', value: 96 }))).level).toBe('critical');
  });

  it('treats storage thresholds as reference values', () => {
    const threshold = first(sensor({ id: '/nvme/0/temperature/5', name: 'Warning Temperature', type: 'Temperature', value: 83 }));
    expect(scoreSensor(threshold).reason).toBe('Reference threshold');
  });

  it('reports a zero CPU temperature as unavailable', () => {
    const zero = first(sensor({ id: '/amdcpu/0/temperature/0', name: 'Core (Tctl/Tdie)', type: 'Temperature', value: 0 }));
    expect(scoreSensor(zero).reason).toBe('CPU telemetry unavailable');
  });

  it('flags a nearly full disk', () => {
    const full = first(sensor({ id: '/ssd/0/load/0', name: 'Used Space', type: 'Load', value: 97 }));
    expect(scoreSensor(full).level).toBe('critical');
  });
});

describe('systemLevel', () => {
  it('keeps unidentified motherboard readings visible without raising system issues (#1)', () => {
    const readings = model(
      sensor({ id: '/lpc/nct6798d/0/temperature/2', name: 'Temperature #3', type: 'Temperature', value: 89.5 }),
      sensor({ id: '/lpc/nct6798d/0/temperature/5', name: 'Temperature #6', type: 'Temperature', value: 81 }));
    expect(readings.sensors.map(s => s.number)).toEqual([89.5, 81]);
    expect(systemIssues(readings)).toEqual([]);
    expect(systemLevel(readings)).toBe('ok');
  });

  it('is nominal when nothing is hot', () => {
    expect(systemLevel(model(sensor({ id: '/amdcpu/0/temperature/0', name: 'Core (Tctl/Tdie)', type: 'Temperature', value: 50 })))).toBe('ok');
  });

  it('escalates to the worst watched sensor', () => {
    expect(systemLevel(model(
      sensor({ id: '/amdcpu/0/temperature/0', name: 'Core (Tctl/Tdie)', type: 'Temperature', value: 80 }),
      sensor({ id: '/gpu-nvidia/0/temperature/0', name: 'GPU Core', type: 'Temperature', value: 92 })))).toBe('critical');
  });
});

describe('CPU helpers', () => {
  it('uses readable Intel package power instead of an unavailable alias or core-only power', () => {
    const m = model(
      sensor({ id: '/intelcpu/0/power/0', name: 'Package', type: 'Power', value: null }),
      sensor({ id: '/intelcpu/0/power/1', name: 'CPU Cores', type: 'Power', value: 20 }),
      sensor({ id: '/intelcpu/0/power/2', name: 'CPU Package', type: 'Power', value: 65 }));
    expect(bestCpuPackagePower(m)?.number).toBe(65);
    expect(bestCpuPackagePower(m)?.text).toBe('CPU Package');
  });

  it('finds total-load aliases without treating a core as the CPU total', () => {
    const m = model(
      sensor({ id: '/intelcpu/0/load/0', name: 'CPU Total', type: 'Load', value: null }),
      sensor({ id: '/intelcpu/0/load/1', name: 'CPU Core #1', type: 'Load', value: 80 }),
      sensor({ id: '/intelcpu/0/load/2', name: 'Total Load', type: 'Load', value: 25 }));
    expect(bestCpuLoad(m)?.number).toBe(25);
  });

  it('falls back from an unavailable package temperature to a readable core', () => {
    const m = model(
      sensor({ id: '/intelcpu/0/temperature/0', name: 'CPU Package', type: 'Temperature', value: null }),
      sensor({ id: '/intelcpu/0/temperature/1', name: 'Core #1', type: 'Temperature', value: 55 }));
    expect(bestCpuTemperature(m)?.text).toBe('Core #1');
  });

  it('prefers Tctl/Tdie over other CPU temperatures', () => {
    const m = model(
      sensor({ id: '/amdcpu/0/temperature/1', name: 'Core #1', type: 'Temperature', value: 90 }),
      sensor({ id: '/amdcpu/0/temperature/0', name: 'Core (Tctl/Tdie)', type: 'Temperature', value: 70 }));
    expect(bestCpuTemperature(m)?.number).toBe(70);
  });

  it('estimates package power from load when the sensor reads zero', () => {
    const m = model(
      sensor({ id: '/amdcpu/0/power/0', name: 'Package', type: 'Power', value: 0 }),
      sensor({ id: '/amdcpu/0/load/0', name: 'CPU Total', type: 'Load', value: 50 }));
    const power = bestCpuPackagePower(m);
    expect(power?.number).toBe(75);
    expect(power?.valueText).toBe('~75 W');
  });
});

describe('chooseStorageTemperature', () => {
  it('uses a readable temperature when the preferred sensor is unavailable', () => {
    const m = model(
      sensor({ id: '/nvme/0/temperature/0', name: 'Composite Temperature', type: 'Temperature', value: null }),
      sensor({ id: '/nvme/0/temperature/1', name: 'Temperature #1', type: 'Temperature', value: 40 }));
    expect(chooseStorageTemperature(m.sensors)?.number).toBe(40);
  });

  it('prefers the composite sensor and ignores thresholds', () => {
    const m = model(
      sensor({ id: '/nvme/0/temperature/6', name: 'Warning Temperature', type: 'Temperature', value: 83 }),
      sensor({ id: '/nvme/0/temperature/0', name: 'Temperature #1', type: 'Temperature', value: 40 }),
      sensor({ id: '/nvme/0/temperature/1', name: 'Composite Temperature', type: 'Temperature', value: 38 }));
    expect(chooseStorageTemperature(m.sensors)?.text).toBe('Composite Temperature');
  });
});
