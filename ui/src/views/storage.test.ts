import { describe, expect, it } from 'vitest';
import { formatBytes } from '../format';
import { Model } from '../model';
import { scoreVolume, systemIssues, systemLevel, volumeUsedPercent } from '../scoring';
import { sensor, snapshot } from '../testing';
import type { Volume } from '../types';

const GB = 1024 ** 3;
const volume = (usedPercent: number, extra: Partial<Volume> = {}): Volume => ({
  id: 'E:',
  label: 'SSD Data',
  fileSystem: 'NTFS',
  totalBytes: 1000 * GB,
  freeBytes: 1000 * GB * (1 - usedPercent / 100),
  disks: ['A', 'B'],
  ...extra
});

describe('formatBytes', () => {
  it('scales to the largest sensible unit', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(3223 * GB)).toBe('3.1 TB');
  });

  it('rejects invalid input', () => {
    expect(formatBytes(-1)).toBe('--');
    expect(formatBytes(Number.NaN)).toBe('--');
  });
});

describe('volume risk', () => {
  it('computes used percentage and clamps it', () => {
    expect(volumeUsedPercent(volume(40))).toBeCloseTo(40, 5);
    expect(volumeUsedPercent({ ...volume(0), freeBytes: -5 })).toBe(100);
    expect(volumeUsedPercent({ ...volume(0), totalBytes: 0 })).toBe(0);
  });

  it('flags nearly full volumes', () => {
    expect(scoreVolume(volume(50)).level).toBe('ok');
    expect(scoreVolume(volume(88)).level).toBe('warn');
    expect(scoreVolume(volume(96)).level).toBe('critical');
  });

  it('reports a full volume as a system issue with a readable title', () => {
    const model = new Model(snapshot([], [], [volume(96)]));

    const [issue] = systemIssues(model);

    expect(issue?.title).toBe('E: SSD Data');
    expect(issue?.detail).toBe('96% used');
    expect(systemLevel(model)).toBe('critical');
  });

  it('prefers volume usage over per-drive Used Space sensors when volumes are known', () => {
    const drive = sensor({ id: '/nvme/0/load/0', name: 'Used Space', type: 'Load', value: 99, device: 'A' });

    expect(systemIssues(new Model(snapshot([drive], [], [volume(10)])))).toHaveLength(0);
    expect(systemIssues(new Model(snapshot([drive])))).toHaveLength(1);
  });
});
