import { describe, expect, it } from 'vitest';
import { categorize, formatMetricName, kilobytesPerSecond, Model, signatureOf } from './model';
import { sensor, snapshot } from './testing';

describe('categorize', () => {
  it.each([
    ['/gpu-nvidia/0/load/0', 'GPU Core', 'Load', 'gpu'],
    ['/amdcpu/0/load/0', 'CPU Total', 'Load', 'cpu'],
    ['/ram/data/0', 'Memory Used', 'Data', 'memory'],
    ['/nvme/0/temperature/0', 'Composite', 'Temperature', 'storage'],
    ['/ahci/1/temperature/0', 'Temperature', 'Temperature', 'storage'],
    ['/nic/%7BA%7D/throughput/7', 'Download Speed', 'Throughput', 'network'],
    ['/lpc/it8665e/0/temperature/1', 'Temperature #1', 'Temperature', 'thermal'],
    ['/lpc/it8665e/0/fan/1', 'Fan #1', 'Fan', 'other']
  ])('%s -> %s', (id, name, type, expected) => {
    expect(categorize(id, name, type)).toBe(expected);
  });
});

describe('formatMetricName', () => {
  it('prefixes the group label unless the name already is the label', () => {
    expect(formatMetricName('Core #1', 'Clocks')).toBe('Clock: Core #1');
    expect(formatMetricName('Temperature', 'Temperatures')).toBe('Temperature');
    expect(formatMetricName('', 'Fans')).toBe('Fan');
  });
});

describe('Model', () => {
  const model = new Model(snapshot(
    [
      sensor({ id: '/amdcpu/0/load/0', name: 'CPU Total', type: 'Load', value: 12.5, unit: '%' }),
      sensor({ id: '/gpu-nvidia/0/load/0', name: 'GPU Core', type: 'Load', value: 9, unit: '%' }),
      sensor({ id: '/lpc/x/voltage/2', name: 'Core (SVI2 TFN)', type: 'Voltage', value: 1.238, unit: 'V', decimals: 3, device: 'AMD Ryzen' })
    ],
    [{ name: 'AMD Ryzen 7', type: 'Cpu' }, { name: 'NVIDIA RTX', type: 'GpuNvidia' }]));

  it('finds sensors by exact text, category and type', () => {
    expect(model.byText('cpu total', 'cpu', 'Load')?.valueText).toBe('12.5 %');
    expect(model.byText('GPU Core', 'cpu')).toBeUndefined();
  });

  it('finds sensors by path fragments', () => {
    expect(model.byContains(['core', 'svi2'], undefined, 'Voltage')?.valueText).toBe('1.238 V');
  });

  it('derives device names from hardware', () => {
    expect(model.cpuName()).toBe('AMD Ryzen 7');
    expect(model.gpuName()).toBe('NVIDIA RTX');
  });

  it('builds a stable change signature', () => {
    const a = snapshot([sensor({ id: 'a', name: 'A', type: 'Load', value: 1 })]);
    const b = snapshot([sensor({ id: 'a', name: 'A', type: 'Load', value: 2 })]);
    expect(signatureOf(a)).toBe(signatureOf(a));
    expect(signatureOf(a)).not.toBe(signatureOf(b));
  });
});

describe('kilobytesPerSecond', () => {
  it('converts bytes per second to KB/s', () => {
    const s = new Model(snapshot([sensor({ id: '/nic/x/throughput/7', name: 'Download Speed', type: 'Throughput', value: 2048 })])).sensors[0];
    expect(kilobytesPerSecond(s)).toBe(2);
    expect(kilobytesPerSecond(undefined)).toBe(0);
  });
});
