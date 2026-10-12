import { describe, expect, it } from 'vitest';
import { Model } from '../model';
import { DEFAULT_POWER, type PowerAssumptions } from '../prefs';
import { sensor, snapshot } from '../testing';
import { estimatePower } from './power';

const assumptions = (change: Partial<PowerAssumptions> = {}): PowerAssumptions => ({ ...DEFAULT_POWER, ...change });

const measured = new Model(snapshot([
  sensor({ id: '/amdcpu/0/power/0', name: 'Package', type: 'Power', value: 100, unit: 'W' }),
  sensor({ id: '/gpu-nvidia/0/power/0', name: 'GPU Package', type: 'Power', value: 200, unit: 'W' })
]));

describe('estimatePower', () => {
  it('uses measured CPU and GPU package power', () => {
    const estimate = estimatePower(measured, assumptions({ platformWatts: 60, monitors: 0, usbWatts: 0, efficiencyPercent: 100 }));

    expect(estimate.measuredDc).toBe(300);
    expect(estimate.internalDc).toBe(360);
    expect(estimate.wall).toBe(360);
  });

  it('divides internal power by PSU efficiency and adds peripherals', () => {
    const estimate = estimatePower(measured, assumptions({ platformWatts: 0, monitors: 2, monitorWatts: 30, usbWatts: 10, efficiencyPercent: 80 }));

    expect(estimate.peripherals).toBe(70);
    expect(estimate.wall).toBeCloseTo(300 / 0.8 + 70, 5);
  });

  it('estimates from load when a power sensor is missing', () => {
    const model = new Model(snapshot([
      sensor({ id: '/amdcpu/0/load/0', name: 'CPU Total', type: 'Load', value: 50, unit: '%' }),
      sensor({ id: '/gpu-nvidia/0/load/9', name: 'GPU Power', type: 'Load', value: 50, unit: '%' })
    ]));

    const estimate = estimatePower(model, assumptions({ platformWatts: 0, monitors: 0, usbWatts: 0, efficiencyPercent: 100 }));

    expect(estimate.measuredDc).toBe(0);
    expect(estimate.internalDc).toBe(35 + 40 + 25 + 160);
  });

  it('counts NVMe and SATA drives separately', () => {
    const model = new Model(snapshot([
      sensor({ id: '/nvme/0/temperature/0', name: 'Composite', type: 'Temperature', value: 40, device: 'NVMe 1' }),
      sensor({ id: '/ahci/0/temperature/0', name: 'Temperature', type: 'Temperature', value: 30, device: 'SATA 1' })
    ]));

    const estimate = estimatePower(model, assumptions({ platformWatts: 0, monitors: 0, usbWatts: 0, efficiencyPercent: 100 }));

    expect(estimate.internalDc - (35 + 25)).toBe(5 + 2);
  });
});
