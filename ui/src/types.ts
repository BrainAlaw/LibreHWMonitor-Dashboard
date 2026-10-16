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

/** A logical volume and the physical drives that back it (several drives = software RAID). */
export interface Volume {
  id: string;
  label: string;
  fileSystem: string;
  totalBytes: number;
  freeBytes: number;
  disks: string[];
}

export interface Snapshot {
  timestamp: string;
  hardware: HardwareInfo[];
  sensors: SnapshotSensor[];
  volumes: Volume[];
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
  problems?: string[];
  driver?: { installed: boolean; version: string | null; error: string | null } | null;
  availableSensorCount?: number;
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
