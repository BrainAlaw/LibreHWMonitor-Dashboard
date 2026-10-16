import { describe, expect, it } from 'vitest';
import { connectionIssue, showHealth } from './header';
import type { Health } from './types';

const health: Health = {
  status: 'degraded', elevated: true, hardwareCount: 2, sensorCount: 10,
  lastError: null, lastUpdate: null, port: 8096, version: '2.0.2'
};

describe('telemetry health details', () => {
  it('shows actionable driver and CPU diagnostics instead of a generic warning', () => {
    showHealth({ ...health, problems: ['PawnIO is not installed', 'CPU temperature unavailable: Intel CPU'] });
    expect(connectionIssue()?.detail).toContain('PawnIO is not installed');
    expect(connectionIssue()?.detail).toContain('CPU temperature unavailable');
  });

  it('supports older health responses and clears warnings after recovery', () => {
    showHealth({ ...health, lastError: 'Driver access failed' });
    expect(connectionIssue()?.detail).toBe('Driver access failed');
    showHealth({ ...health, status: 'ok' });
    expect(connectionIssue()).toBeNull();
  });
});
