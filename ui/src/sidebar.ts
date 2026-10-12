import { getSettings, saveSettings } from './api';
import { el, setText } from './dom';
import type { Health, HealthStatus } from './types';

const STATUS_LABEL: Record<HealthStatus, string> = {
  starting: 'Starting...',
  ok: 'Telemetry OK',
  degraded: 'Telemetry degraded',
  error: 'Telemetry error'
};

export function showHealth(health: Health): void {
  el('healthBox').dataset['state'] = health.status;
  setText('healthText', STATUS_LABEL[health.status]);
  const parts = [`${health.sensorCount} sensors`, health.elevated ? 'admin' : 'NOT elevated'];
  if (health.lastError) parts.push(health.lastError);
  setText('healthDetail', parts.join(' | '));
}

export function showDisconnected(): void {
  el('healthBox').dataset['state'] = 'error';
  setText('healthText', 'Server unreachable');
  setText('healthDetail', 'Reconnecting...');
}

export async function initSettingsPanel(): Promise<void> {
  const port = el<HTMLInputElement>('setPort');
  const sampling = el<HTMLSelectElement>('setSampling');
  const autostart = el<HTMLInputElement>('setAutostart');
  const minimized = el<HTMLInputElement>('setMinimized');
  const closeToTray = el<HTMLInputElement>('setCloseToTray');

  try {
    const current = await getSettings();
    port.value = String(current.port);
    sampling.value = String(current.samplingMs);
    autostart.checked = current.autostart;
    minimized.checked = current.startMinimized;
    closeToTray.checked = current.closeToTray;
  } catch {
    setText('settingsMsg', 'Settings unavailable');
  }

  el('saveSettings').addEventListener('click', async () => {
    try {
      const result = await saveSettings({
        port: Number(port.value),
        samplingMs: Number(sampling.value),
        autostart: autostart.checked,
        startMinimized: minimized.checked,
        closeToTray: closeToTray.checked
      });
      setText('settingsMsg', result.ok
        ? (result.restartRequired ? 'Saved. Restart to apply the port.' : 'Saved.')
        : (result.error ?? 'Save failed'));
    } catch {
      setText('settingsMsg', 'Save failed');
    }
  });
}
