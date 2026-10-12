import type { Snapshot, SnapshotSensor } from './types';

/** Test helper: builds a sensor with sensible defaults. */
export function sensor(partial: Partial<SnapshotSensor> & Pick<SnapshotSensor, 'id' | 'name' | 'type'>): SnapshotSensor {
  return {
    device: 'Device',
    path: [],
    group: partial.type === 'Temperature' ? 'Temperatures' : partial.type === 'Load' ? 'Load' : `${partial.type}s`,
    value: 0,
    min: null,
    max: null,
    unit: '',
    decimals: 1,
    ...partial
  };
}

export function snapshot(sensors: SnapshotSensor[], hardware: Snapshot['hardware'] = []): Snapshot {
  return { timestamp: '2026-01-01T00:00:00Z', hardware, sensors };
}
