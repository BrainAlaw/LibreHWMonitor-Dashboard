import { formatValue } from './format';
import type { Category, HardwareInfo, Sensor, Snapshot, SnapshotSensor, Volume } from './types';

const GROUP_LABELS: Record<string, string> = {
  Temperatures: 'Temperature',
  Powers: 'Power',
  Clocks: 'Clock',
  Load: 'Load',
  Levels: 'Health',
  Data: 'Data',
  Throughput: 'Throughput',
  Fans: 'Fan',
  Controls: 'Control',
  Voltages: 'Voltage',
  Factors: 'Factor'
};

export function categorize(id: string, name: string, type: string): Category {
  const sensorId = id.toLowerCase();
  const text = name.toLowerCase();
  if (sensorId.includes('/gpu-') || text.includes('gpu') || text.includes('d3d')) return 'gpu';
  if (sensorId.includes('/amdcpu') || sensorId.includes('/intelcpu') || text.includes('cpu') || text.includes('core #')) return 'cpu';
  if (sensorId.includes('/ram') || text.includes('memory') || text.includes('virtual memory')) return 'memory';
  if (/\/(nvme|ssd|hdd|ahci)\//.test(sensorId) || text.includes('used space') || text.includes('remaining life')) return 'storage';
  if (sensorId.includes('/nic') || text.includes('network') || text.includes('download') || text.includes('upload')) return 'network';
  if (type.toLowerCase().includes('temperature')) return 'thermal';
  return 'other';
}

export function formatMetricName(text: string, group: string): string {
  const label = GROUP_LABELS[group] ?? group;
  if (!label) return text || 'Sensor';
  if (text && text.toLowerCase() === label.toLowerCase()) return label;
  return text ? `${label}: ${text}` : label;
}

export function toSensor(raw: SnapshotSensor): Sensor {
  return {
    id: raw.id,
    type: raw.type,
    text: raw.name,
    displayName: formatMetricName(raw.name, raw.group),
    deviceName: raw.device,
    groupName: raw.group,
    category: categorize(raw.id, raw.name, raw.type),
    pathText: [raw.device, ...raw.path, raw.group, raw.name].join(' / '),
    valueText: formatValue(raw.value, raw.unit, raw.decimals, raw.type),
    minText: formatValue(raw.min, raw.unit, raw.decimals, raw.type),
    maxText: formatValue(raw.max, raw.unit, raw.decimals, raw.type),
    number: raw.value,
    minNumber: raw.min,
    maxNumber: raw.max
  };
}

/** Throughput sensors carry bytes per second; the dashboard sums and charts KB/s. */
export function kilobytesPerSecond(sensor: Sensor | undefined): number {
  return sensor?.number != null ? sensor.number / 1024 : 0;
}

export class Model {
  readonly hardware: HardwareInfo[];
  readonly sensors: Sensor[];
  readonly volumes: Volume[];

  constructor(snapshot: Snapshot) {
    this.hardware = snapshot.hardware;
    this.sensors = snapshot.sensors.map(toSensor);
    this.volumes = snapshot.volumes ?? [];
  }

  byText(text: string, category?: Category, type?: string): Sensor | undefined {
    const wanted = text.toLowerCase();
    return this.sensors.find(s =>
      s.text.toLowerCase() === wanted &&
      (!category || s.category === category) &&
      (!type || s.type === type));
  }

  byContains(parts: string[], category?: Category, type?: string): Sensor | undefined {
    return this.sensors.find(s => {
      const haystack = `${s.text} ${s.pathText}`.toLowerCase();
      return parts.every(part => haystack.includes(part.toLowerCase())) &&
        (!category || s.category === category) &&
        (!type || s.type === type);
    });
  }

  cpuName(): string {
    return this.hardware.find(h => h.type === 'Cpu')?.name ?? 'CPU';
  }

  gpuName(): string {
    return this.hardware.find(h => h.type.startsWith('Gpu'))?.name ?? 'GPU';
  }
}

export function signatureOf(snapshot: Snapshot): string {
  return snapshot.sensors.map(s => `${s.id}:${s.value}:${s.min}:${s.max}`).join('|');
}
