import { describe, expect, it } from 'vitest';
import { clamp, formatRate, formatValue } from './format';

describe('formatValue', () => {
  it('appends the unit and uses fixed decimals', () => {
    expect(formatValue(44.5, '°C', 1, 'Temperature')).toBe('44.5 °C');
    expect(formatValue(4450, 'MHz', 1, 'Clock')).toBe('4450.0 MHz');
  });

  it('omits the unit for unitless factors', () => {
    expect(formatValue(44.5, '', 3, 'Factor')).toBe('44.500');
  });

  it('returns an empty string for missing or non-finite values', () => {
    expect(formatValue(null, 'W', 1, 'Power')).toBe('');
    expect(formatValue(Number.NaN, 'W', 1, 'Power')).toBe('');
  });

  it('scales throughput from bytes per second', () => {
    expect(formatValue(0, 'B/s', 0, 'Throughput')).toBe('0 KB/s');
    expect(formatValue(215_450, 'B/s', 0, 'Throughput')).toBe('210.4 KB/s');
    expect(formatValue(40.5 * 1024 * 1024, 'B/s', 0, 'Throughput')).toBe('40.5 MB/s');
  });
});

describe('formatRate', () => {
  it('picks KB, MB or GB per second', () => {
    expect(formatRate(512)).toBe('512 KB/s');
    expect(formatRate(2048)).toBe('2.0 MB/s');
    expect(formatRate(3 * 1024 * 1024)).toBe('3.00 GB/s');
  });

  it('handles empty and invalid input', () => {
    expect(formatRate(0)).toBe('0 KB/s');
    expect(formatRate(Number.NaN)).toBe('0 KB/s');
  });
});

describe('clamp', () => {
  it('limits values to the range', () => {
    expect(clamp(120, 0, 100)).toBe(100);
    expect(clamp(-5, 0, 100)).toBe(0);
    expect(clamp(42, 0, 100)).toBe(42);
  });
});
