import { el, setText } from './dom';
import type { Health, HealthStatus } from './types';

const LABEL: Record<HealthStatus, string> = {
  starting: 'Starting...',
  ok: 'Live',
  degraded: 'Limited',
  error: 'Telemetry error'
};

export function showHealth(health: Health): void {
  const pill = el('statusPill');
  pill.dataset['state'] = health.status;
  pill.title = health.lastError ?? '';
  setText('healthText', LABEL[health.status]);

  const detail = [`${health.sensorCount} sensors`];
  if (!health.elevated) detail.push('not admin');
  setText('healthDetail', health.status === 'error' && health.lastError ? health.lastError : detail.join(' · '));
}

export function showDisconnected(): void {
  el('statusPill').dataset['state'] = 'error';
  setText('healthText', 'Disconnected');
  setText('healthDetail', 'reconnecting...');
}
