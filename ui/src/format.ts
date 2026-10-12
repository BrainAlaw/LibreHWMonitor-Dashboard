export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

export function formatRate(kilobytesPerSecond: number, kbDecimals = 0): string {
  if (!Number.isFinite(kilobytesPerSecond) || kilobytesPerSecond <= 0) return '0 KB/s';
  if (kilobytesPerSecond >= 1024 * 1024) return `${(kilobytesPerSecond / 1024 / 1024).toFixed(2)} GB/s`;
  if (kilobytesPerSecond >= 1024) return `${(kilobytesPerSecond / 1024).toFixed(1)} MB/s`;
  return `${kilobytesPerSecond.toFixed(kbDecimals)} KB/s`;
}

/** Formats a sensor value the way the user expects to read it; throughput scales to KB/MB/GB. */
export function formatValue(value: number | null, unit: string, decimals: number, type: string): string {
  if (value === null || !Number.isFinite(value)) return '';
  if (type === 'Throughput') return formatRate(value / 1024, 1);
  const number = value.toFixed(decimals);
  return unit ? `${number} ${unit}` : number;
}

/** Binary units with one decimal place, e.g. "2.8 TB". */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '--';
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${unit === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unit]}`;
}
