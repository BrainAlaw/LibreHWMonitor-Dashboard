import { describe, expect, it } from 'vitest';
import { Model } from '../model';
import { sensor, snapshot } from '../testing';
import { adapters, isVirtualAdapter } from './network';

const nic = (device: string, down: number, up: number) => [
  sensor({ id: `/nic/${device}/throughput/7`, name: 'Download Speed', type: 'Throughput', value: down, device }),
  sensor({ id: `/nic/${device}/throughput/8`, name: 'Upload Speed', type: 'Throughput', value: up, device })
];

describe('adapters', () => {
  it('drops filter-driver instances that mirror a real adapter', () => {
    const model = new Model(snapshot([
      ...nic('Ethernet 2', 2048, 1024),
      ...nic('Ethernet 2-QoS Packet Scheduler-0000', 2048, 1024),
      ...nic('Ethernet 2-WFP Native MAC Layer LightWeight Filter-0000', 2048, 1024)
    ]));

    expect(adapters(model).map(a => a.name)).toEqual(['Ethernet 2']);
  });

  it('reports speeds in KB/s from bytes per second and sorts by traffic', () => {
    const model = new Model(snapshot([...nic('Slow', 1024, 0), ...nic('Fast', 4096, 2048)]));

    const list = adapters(model);

    expect(list.map(a => a.name)).toEqual(['Fast', 'Slow']);
    expect(list[0]).toMatchObject({ down: 4, up: 2 });
  });

  it('flags virtual adapters so tunnels are not double counted', () => {
    expect(isVirtualAdapter('SurfsharkWireGuard')).toBe(true);
    expect(isVirtualAdapter('vEthernet (Default Switch)')).toBe(true);
    expect(isVirtualAdapter('Local Area Connection* 6')).toBe(true);
    expect(isVirtualAdapter('Ethernet 2')).toBe(false);
    expect(isVirtualAdapter('Wi-Fi')).toBe(false);
  });
});
