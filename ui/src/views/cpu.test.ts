import { describe, expect, it } from 'vitest';
import { coreColumns } from './cpu';

describe('coreColumns', () => {
  it.each([
    [1, 1],
    [2, 2],
    [4, 2],
    [6, 3],
    [8, 4],
    [12, 4],
    [16, 4],
    [24, 6],
    [32, 8],
    [64, 8]
  ])('%i cores -> %i columns', (cores, columns) => {
    expect(coreColumns(cores)).toBe(columns);
  });

  it('never leaves more than one empty row for typical counts', () => {
    for (const count of [3, 5, 7, 10, 14, 20, 28, 48]) {
      const columns = coreColumns(count);
      expect(Math.ceil(count / columns) * columns - count).toBeLessThan(columns);
    }
  });
});
