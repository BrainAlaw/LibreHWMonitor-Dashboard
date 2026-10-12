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
