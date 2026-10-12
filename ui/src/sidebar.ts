import { getSettings, saveSettings, type SettingsUpdate } from './api';
import { el, setHtml, setText } from './dom';
import { h } from './html';
import type { Health, HealthStatus, LanUrl } from './types';

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

function renderLanUrls(allowLan: boolean, urls: LanUrl[]): void {
  el('lanBox').hidden = !allowLan;
  setHtml('lanUrls', allowLan && urls.length === 0
    ? h`<div class="path">No private network address found.</div>`
    : h`${urls.map(entry => h`<div class="lan-row">
        <span class="path" title="${entry.name}">${entry.name}</span>
        <input readonly value="${entry.url}" aria-label="LAN link for ${entry.name}">
        <button class="btn" data-copy="${entry.url}">Copy</button>
      </div>`)}`);
}

export async function initSettingsPanel(): Promise<void> {
  const port = el<HTMLInputElement>('setPort');
  const sampling = el<HTMLSelectElement>('setSampling');
  const autostart = el<HTMLInputElement>('setAutostart');
  const minimized = el<HTMLInputElement>('setMinimized');
  const closeToTray = el<HTMLInputElement>('setCloseToTray');
  const allowLan = el<HTMLInputElement>('setAllowLan');

  try {
    const current = await getSettings();
    port.value = String(current.port);
    sampling.value = String(current.samplingMs);
    autostart.checked = current.autostart;
    minimized.checked = current.startMinimized;
    closeToTray.checked = current.closeToTray;
    allowLan.checked = current.allowLan;
    renderLanUrls(current.allowLan, current.lanUrls);
  } catch {
    // Settings are served to this computer only; LAN viewers just get the dashboard.
    el('settingsBox').hidden = true;
    return;
  }

  const collect = (regenerateToken = false): SettingsUpdate => ({
    port: Number(port.value),
    samplingMs: Number(sampling.value),
    autostart: autostart.checked,
    startMinimized: minimized.checked,
    closeToTray: closeToTray.checked,
    allowLan: allowLan.checked,
    regenerateToken
  });

  async function save(update: SettingsUpdate): Promise<void> {
    try {
      const result = await saveSettings(update);
      if (!result.ok) {
        setText('settingsMsg', result.error ?? 'Save failed');
        return;
      }

      const parts = ['Saved.'];
      if (result.restartRequired) parts.push('Restart PC Monitor to apply port/LAN changes.');
      if (!result.firewallOk) parts.push('Could not update the Windows Firewall rule.');
      setText('settingsMsg', parts.join(' '));

      const fresh = await getSettings();
      renderLanUrls(fresh.allowLan, fresh.lanUrls);
    } catch {
      setText('settingsMsg', 'Save failed');
    }
  }

  el('saveSettings').addEventListener('click', () => void save(collect()));
  el('newToken').addEventListener('click', () => void save(collect(true)));

  el('lanUrls').addEventListener('click', event => {
    const button = (event.target as Element).closest<HTMLElement>('[data-copy]');
    const text = button?.dataset['copy'];
    if (!button || !text) return;
    void navigator.clipboard.writeText(text).then(
      () => setText('settingsMsg', 'Link copied.'),
      () => setText('settingsMsg', 'Copy failed - select the link and copy it manually.'));
  });
}
