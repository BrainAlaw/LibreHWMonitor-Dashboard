import { describe, expect, it } from 'vitest';
import { cpuThermalTile } from './components';
import { Model } from './model';
import { sensor, snapshot } from './testing';

describe('CPU temperature labels', () => {
  it('labels Intel package temperature using its actual sensor name', () => {
    const reading = new Model(snapshot([
      sensor({ id: '/intelcpu/0/temperature/0', name: 'CPU Package', type: 'Temperature', value: 55 })
    ])).sensors[0]!;
    expect(cpuThermalTile(reading).html).toContain('CPU Package');
    expect(cpuThermalTile(reading).html).not.toContain('Tctl/Tdie');
  });

  it('directs missing temperature readings to system diagnostics', () => {
    const html = cpuThermalTile(undefined).html;
    expect(html).toContain('CPU temperature');
    expect(html).toContain('Check System status');
    expect(html).not.toContain('Run as Administrator');
  });
});
