import { describe, expect, it } from 'vitest';
import { allCoolingSensors, fanKey, spinSeconds } from './fans';
import { Model } from './model';
import { sensor, snapshot } from './testing';

const fan = (value: number | null, name = 'Fan #1', max: number | null = null) =>
  new Model(snapshot([sensor({ id: '/lpc/x/fan/0', name, type: 'Fan', value, max, device: 'Board', unit: 'RPM', decimals: 0 })])).sensors[0]!;

describe('spinSeconds', () => {
  it('does not spin a stopped or unreadable fan', () => {
    expect(spinSeconds(fan(0))).toBeNull();
    expect(spinSeconds(fan(null))).toBeNull();
  });

  it('spins faster at higher RPM', () => {
    expect(spinSeconds(fan(2000))!).toBeLessThan(spinSeconds(fan(500))!);
  });

  it('stays within a readable range', () => {
    expect(spinSeconds(fan(100_000))).toBe(0.3);
    expect(spinSeconds(fan(10))).toBe(8);
  });

  it('is stable against small sensor noise', () => {
    expect(spinSeconds(fan(1000))).toBe(spinSeconds(fan(1004)));
  });

  it('maps duty-cycle controls onto the same scale', () => {
    const control = new Model(snapshot([sensor({ id: '/c/0', name: 'Pump', type: 'Control', value: 100, unit: '%' })])).sensors[0]!;
    expect(spinSeconds(control)).toBeCloseTo(1.2, 5);
  });
});

describe('cooling sensors', () => {
  it('builds a stable key from the device and fan number', () => {
    expect(fanKey(fan(900, 'Fan #4'))).toBe('Board:#4');
  });

  it('ignores idle fans and prefers RPM over duty cycle for the same fan', () => {
    const model = new Model(snapshot([
      sensor({ id: '/a/fan/0', name: 'Fan #1', type: 'Fan', value: 800, device: 'Board' }),
      sensor({ id: '/a/control/0', name: 'Fan #1', type: 'Control', value: 50, device: 'Board' }),
      sensor({ id: '/a/fan/1', name: 'Fan #2', type: 'Fan', value: 0, max: 0, device: 'Board' })
    ]));

    const fans = allCoolingSensors(model);

    expect(fans).toHaveLength(1);
    expect(fans[0]?.type).toBe('Fan');
  });
});
