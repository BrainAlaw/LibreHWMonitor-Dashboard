import { clamp } from './format';
import type { Model } from './model';
import type { Level, Risk, Sensor, Volume } from './types';

export function colorForPercent(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return 'var(--muted-2)';
  if (value >= 90) return 'var(--red)';
  if (value >= 75) return 'var(--yellow)';
  return 'var(--green)';
}

export function tempColor(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return 'var(--muted-2)';
  if (value >= 85) return 'var(--red)';
  if (value >= 70) return 'var(--yellow)';
  return 'var(--green)';
}

export function isPercentLike(sensor: Sensor): boolean {
  return /%$/.test(sensor.valueText.trim()) || ['Load', 'Control', 'Level'].includes(sensor.type);
}

export function isInvalidCpuTelemetry(sensor: Sensor | undefined): boolean {
  if (!sensor || sensor.category !== 'cpu') return false;
  if (sensor.number === null) return true;
  if (sensor.type === 'Temperature' && sensor.number === 0) return true;
  if (sensor.type === 'Power' && sensor.number === 0) return true;
  if ((sensor.type === 'Clock' || sensor.type === 'Factor') && sensor.number === null) return true;
  return false;
}

export function isStorageTemperatureThreshold(sensor: Sensor | undefined): boolean {
  return sensor?.category === 'storage' &&
    sensor.type === 'Temperature' &&
    /warning temperature|critical temperature/i.test(sensor.text);
}

export function isLiveTemperature(sensor: Sensor | undefined): boolean {
  return sensor?.type === 'Temperature' && !isStorageTemperatureThreshold(sensor);
}

function cpuTemperatureRank(sensor: Sensor): number {
  const text = sensor.text.toLowerCase();
  if (text.includes('tctl') || text.includes('tdie')) return 0;
  if (text.includes('package')) return 1;
  if (text.includes('core')) return 2;
  return 3;
}

export function bestCpuTemperature(model: Model): Sensor | undefined {
  const preferred = model.byText('Core (Tctl/Tdie)', 'cpu', 'Temperature');
  if (preferred && (preferred.number ?? 0) > 0) return preferred;

  return model.sensors
    .filter(s => s.category === 'cpu' && isLiveTemperature(s) && (s.number ?? 0) > 0)
    .sort((a, b) => cpuTemperatureRank(a) - cpuTemperatureRank(b) || (b.number ?? 0) - (a.number ?? 0))[0];
}

/** Package aliases used by Intel and AMD; core/SoC power is not a package total. */
export function measuredCpuPackagePower(model: Model): Sensor | undefined {
  return model.sensors.find(s => s.category === 'cpu' && s.type === 'Power' &&
    /^(?:CPU\s+)?Package(?:\s+Power)?$/i.test(s.text) && (s.number ?? 0) > 0);
}

export function bestCpuLoad(model: Model): Sensor | undefined {
  return model.sensors.find(s => s.category === 'cpu' && s.type === 'Load' &&
    /^(?:CPU\s+)?(?:Total|Total Load)$/i.test(s.text) && s.number !== null);
}

/** Measured package power, or an estimate derived from CPU load when the sensor reads zero. */
export function bestCpuPackagePower(model: Model): Sensor | undefined {
  const measured = measuredCpuPackagePower(model);
  if (measured) return measured;

  const cpuLoad = bestCpuLoad(model);
  if (!cpuLoad || cpuLoad.number === null) return measured;

  const estimate = 35 + (clamp(cpuLoad.number, 0, 100) / 100) * 80;
  const base = measured ?? cpuLoad;
  return {
    ...base,
    text: 'Package Estimate',
    displayName: 'Power: Package Estimate',
    valueText: `~${estimate.toFixed(0)} W`,
    number: estimate
  };
}

const NORMAL: Risk = { score: 100, level: 'ok', reason: 'Normal' };

/** Generic Super I/O channels have no confirmed mapping to a physical component. */
export function isUnidentifiedMotherboardTemperature(sensor: Sensor): boolean {
  return sensor.type === 'Temperature' &&
    sensor.id.toLowerCase().startsWith('/lpc/') &&
    /^Temperature\s*#\s*\d+$/i.test(sensor.text.trim());
}

export function scoreSensor(sensor: Sensor): Risk {
  const n = sensor.number;
  if (isUnidentifiedMotherboardTemperature(sensor)) {
    return { score: 100, level: 'ok', reason: 'Unidentified sensor; alerts disabled' };
  }
  if (isInvalidCpuTelemetry(sensor)) return { score: 65, level: 'warn', reason: 'CPU telemetry unavailable' };
  if (n === null) return { score: 100, level: 'ok', reason: 'Reading unavailable' };
  if (/threshold/i.test(sensor.text) || isStorageTemperatureThreshold(sensor)) {
    return { score: 100, level: 'ok', reason: 'Reference threshold' };
  }

  if (sensor.type === 'Temperature') {
    const hotSpot = sensor.text.includes('Hot Spot');
    const storage = sensor.category === 'storage';
    const critical = storage ? 70 : hotSpot ? 95 : 90;
    const warning = storage ? 60 : hotSpot ? 83 : 78;
    if (n >= critical) return { score: 20, level: 'critical', reason: 'Critical temperature' };
    if (n >= warning) return { score: 60, level: 'warn', reason: 'High temperature' };
  }

  if (sensor.type === 'Load' && /used space/i.test(sensor.text)) {
    if (n >= 95) return { score: 15, level: 'critical', reason: 'Storage almost full' };
    if (n >= 85) return { score: 60, level: 'warn', reason: 'Storage filling up' };
  }

  if (sensor.type === 'Load' && n >= 98 && !/core #/i.test(sensor.text)) {
    return { score: 70, level: 'warn', reason: 'High utilization' };
  }

  if (sensor.type === 'Level' && /percentage used/i.test(sensor.text)) {
    if (n >= 80) return { score: 25, level: 'critical', reason: 'Storage endurance consumed' };
    if (n >= 50) return { score: 60, level: 'warn', reason: 'Storage endurance aging' };
  }

  if (sensor.type === 'Level' && /remaining life|available spare/i.test(sensor.text)) {
    if (n <= 10) return { score: 25, level: 'critical', reason: 'Storage endurance low' };
    if (n <= 30) return { score: 60, level: 'warn', reason: 'Storage endurance reduced' };
  }

  return NORMAL;
}

export interface SystemIssue {
  title: string;
  detail: string;
  risk: Risk;
}

export function scoreVolume(volume: Volume): Risk {
  const used = volumeUsedPercent(volume);
  if (used >= 95) return { score: 15, level: 'critical', reason: 'Volume almost full' };
  if (used >= 85) return { score: 60, level: 'warn', reason: 'Volume filling up' };
  return NORMAL;
}

export function volumeUsedPercent(volume: Volume): number {
  return volume.totalBytes > 0 ? clamp(((volume.totalBytes - volume.freeBytes) / volume.totalBytes) * 100, 0, 100) : 0;
}

/** Everything currently outside its normal range, worst first. */
export function systemIssues(model: Model): SystemIssue[] {
  // Volumes know the real usage of RAID sets, which per-drive "Used Space" sensors cannot report.
  const hasVolumes = model.volumes.length > 0;
  const sensorIssues = watchedSensors(model)
    .filter(sensor => !(hasVolumes && sensor.type === 'Load' && /used space/i.test(sensor.text)))
    .map(sensor => ({ title: `${sensor.deviceName}: ${sensor.text}`, detail: sensor.valueText, risk: scoreSensor(sensor) }));
  const volumeIssues = model.volumes.map(volume => ({
    title: `${volume.id} ${volume.label}`.trim(),
    detail: `${Math.round(volumeUsedPercent(volume))}% used`,
    risk: scoreVolume(volume)
  }));

  return [...sensorIssues, ...volumeIssues]
    .filter(issue => issue.risk.level !== 'ok')
    .sort((a, b) => a.risk.score - b.risk.score);
}

export function systemLevel(model: Model): Level {
  const levels = systemIssues(model).map(issue => issue.risk.level);
  if (levels.includes('critical')) return 'critical';
  return levels.includes('warn') ? 'warn' : 'ok';
}

function watchedSensors(model: Model): Sensor[] {
  return model.sensors.filter(s =>
    isLiveTemperature(s) ||
    (s.category === 'cpu' && s.type === 'Power' && /^(?:CPU\s+)?Package(?:\s+Power)?$/i.test(s.text)) ||
    (s.category === 'cpu' && s.type === 'Clock' && s.text === 'Core #1') ||
    (s.type === 'Load' && (/used space/i.test(s.text) || (s.number ?? 0) >= 90)) ||
    (s.type === 'Level' && /remaining life|available spare|percentage used/i.test(s.text)));
}

export function chooseStorageTemperature(sensors: Sensor[]): Sensor | undefined {
  const rank = (sensor: Sensor): number => {
    const text = sensor.text.toLowerCase();
    if (text.includes('composite')) return 0;
    if (text.includes('temperature #1')) return 1;
    if (text.includes('temperature #2')) return 2;
    if (text.includes('warning')) return 3;
    if (text.includes('critical')) return 4;
    return 5;
  };
  return sensors.filter(s => isLiveTemperature(s) && s.number !== null).sort((a, b) => rank(a) - rank(b))[0];
}

export function hardwareKind(sensor: Sensor): string {
  const id = sensor.id;
  if (id.startsWith('/nvme/')) return 'NVMe';
  if (id.startsWith('/ssd/') || id.startsWith('/ahci/')) return 'SSD';
  if (id.startsWith('/hdd/')) return 'HDD';
  if (id.startsWith('/nic/')) return 'NIC';
  if (id.startsWith('/gpu-')) return 'GPU';
  if (id.includes('cpu')) return 'CPU';
  return sensor.category;
}
