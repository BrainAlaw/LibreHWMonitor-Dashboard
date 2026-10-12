/** Wire format of /api/snapshot and the SSE "snapshot" event. */
export interface SnapshotSensor {
  id: string;
  device: string;
  path: string[];
  group: string;
  name: string;
  type: string;
  value: number | null;
  min: number | null;
  max: number | null;
  unit: string;
  decimals: number;
}

export interface HardwareInfo {
  name: string;
  type: string;
}

export interface Snapshot {
  host: string;
  timestamp: string;
  hardware: HardwareInfo[];
  sensors: SnapshotSensor[];
}

export type HealthStatus = 'starting' | 'ok' | 'degraded' | 'error';

export interface Health {
  status: HealthStatus;
  elevated: boolean;
  hardwareCount: number;
  sensorCount: number;
  lastError: string | null;
  lastUpdate: string | null;
  port: number;
  version: string | null;
}

export interface SettingsView {
  port: number;
  samplingMs: number;
  startMinimized: boolean;
  closeToTray: boolean;
  autostart: boolean;
  allowLan: boolean;
  lanUrls: LanUrl[];
}

export interface LanUrl {
  name: string;
  url: string;
}

export type Category = 'gpu' | 'cpu' | 'memory' | 'storage' | 'network' | 'thermal' | 'other';

export type Level = 'ok' | 'warn' | 'critical';

/** A sensor enriched with everything the views need. */
export interface Sensor {
  id: string;
  type: string;
  text: string;
  displayName: string;
  deviceName: string;
  groupName: string;
  category: Category;
  pathText: string;
  valueText: string;
  minText: string;
  maxText: string;
  number: number | null;
  minNumber: number | null;
  maxNumber: number | null;
}

export interface Risk {
  score: number;
  level: Level;
  reason: string;
}
