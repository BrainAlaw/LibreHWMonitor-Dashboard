import { getSettings, saveSettings, type SettingsUpdate } from './api';
import { el, setHtml } from './dom';
import { allCoolingSensors, fanKey } from './fans';
import { h } from './html';
import type { Model } from './model';
import { prefs, updatePrefs } from './prefs';
import { toast } from './toast';
import type { LanUrl } from './types';

const SAVE_DEBOUNCE_MS = 450;

const input = (id: string): HTMLInputElement => el<HTMLInputElement>(id);

function renderLanUrls(allowLan: boolean, urls: LanUrl[]): void {
  el('lanBox').hidden = !allowLan;
  setHtml('lanUrls', allowLan && urls.length === 0
    ? h`<div class="settings-note">No private network address found.</div>`
    : h`${urls.map(entry => h`<div class="lan-row" data-key="${entry.name}">
        <span class="name" title="${entry.name}">${entry.name}</span>
        <input readonly value="${entry.url}" aria-label="LAN link for ${entry.name}">
        <button class="btn" data-copy="${entry.url}">Copy</button>
      </div>`)}`);
}

/** Display preferences and power assumptions live in this browser; they work for LAN viewers too. */
function initLocalPrefs(): void {
  const power = prefs().power;
  input('setShowRaw').checked = prefs().showRaw;
  input('pwMonitors').value = String(power.monitors);
  input('pwMonitorWatts').value = String(power.monitorWatts);
  input('pwUsb').value = String(power.usbWatts);
  input('pwPlatform').value = String(power.platformWatts);
  input('pwEfficiency').value = String(power.efficiencyPercent);

  input('setShowRaw').addEventListener('change', () => updatePrefs(p => { p.showRaw = input('setShowRaw').checked; }));

  const numeric: Array<[string, keyof ReturnType<typeof prefs>['power']]> = [
    ['pwMonitors', 'monitors'],
    ['pwMonitorWatts', 'monitorWatts'],
    ['pwUsb', 'usbWatts'],
    ['pwPlatform', 'platformWatts'],
    ['pwEfficiency', 'efficiencyPercent']
  ];
  for (const [id, key] of numeric) {
    input(id).addEventListener('change', () => updatePrefs(p => { p.power[key] = Number(input(id).value); }));
  }
}

/** Server-side settings (this PC only). Every change is saved automatically. */
async function initServerSettings(): Promise<void> {
  let current;
  try {
    current = await getSettings();
  } catch {
    // Served to this computer only; LAN viewers just do not get these sections.
    document.querySelectorAll<HTMLElement>('[data-local-only]').forEach(section => { section.hidden = true; });
    return;
  }

  input('setPort').value = String(current.port);
  el<HTMLSelectElement>('setSampling').value = String(current.samplingMs);
  input('setAutostart').checked = current.autostart;
  input('setMinimized').checked = current.startMinimized;
  input('setCloseToTray').checked = current.closeToTray;
  input('setAllowLan').checked = current.allowLan;
  renderLanUrls(current.allowLan, current.lanUrls);

  const collect = (regenerateToken = false): SettingsUpdate => ({
    port: Number(input('setPort').value),
    samplingMs: Number(el<HTMLSelectElement>('setSampling').value),
    autostart: input('setAutostart').checked,
    startMinimized: input('setMinimized').checked,
    closeToTray: input('setCloseToTray').checked,
    allowLan: input('setAllowLan').checked,
    regenerateToken
  });

  async function save(update: SettingsUpdate): Promise<void> {
    try {
      const result = await saveSettings(update);
      if (!result.ok) {
        toast(result.error ?? 'Could not save settings', 'error');
        return;
      }

      toast(result.restartRequired ? 'Saved - restart PC Monitor to apply port/LAN changes' : 'Saved', result.restartRequired ? 'warn' : 'ok');
      if (!result.firewallOk) toast('Could not update the Windows Firewall rule', 'warn');

      const fresh = await getSettings();
      renderLanUrls(fresh.allowLan, fresh.lanUrls);
    } catch {
      toast('Could not save settings', 'error');
    }
  }

  let timer: number | undefined;
  const scheduleSave = (): void => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void save(collect()), SAVE_DEBOUNCE_MS);
  };

  for (const id of ['setPort', 'setSampling', 'setAutostart', 'setMinimized', 'setCloseToTray', 'setAllowLan']) {
    el(id).addEventListener('change', scheduleSave);
  }

  el('newToken').addEventListener('click', () => void save(collect(true)));
  el('lanUrls').addEventListener('click', event => {
    const text = (event.target as Element).closest<HTMLElement>('[data-copy]')?.dataset['copy'];
    if (!text) return;
    void navigator.clipboard.writeText(text).then(
      () => toast('Link copied', 'ok'),
      () => toast('Copy failed - select the link and copy it manually', 'error'));
  });
}

function renderFanList(model: Model | null): void {
  const fans = model ? allCoolingSensors(model) : [];
  const hidden = new Set(prefs().hiddenFans);
  const shown = fans.filter(fan => !hidden.has(fanKey(fan))).length;
  el('fanSummary').textContent = fans.length ? `${shown} of ${fans.length} shown` : 'no fans reported yet';

  setHtml('fanList', h`${fans.map(fan => {
    const key = fanKey(fan);
    return h`<label class="row" data-key="${key}">
      <span>${fan.text}<small title="${fan.deviceName}">${fan.deviceName}</small></span>
      <span class="switch"><input type="checkbox" data-fan="${key}" ${hidden.has(key) ? '' : 'checked'}><i></i></span>
    </label>`;
  })}`);
}

/** Wires the settings popover. `getModel` supplies the latest snapshot for the fan list. */
export function initSettings(getModel: () => Model | null): { refreshFans(): void } {
  const button = el('settingsButton');
  const popover = el('settingsPopover');

  const setOpen = (open: boolean): void => {
    popover.hidden = !open;
    button.setAttribute('aria-expanded', String(open));
    if (open) renderFanList(getModel());
  };

  button.addEventListener('click', () => setOpen(Boolean(popover.hidden)));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') setOpen(false);
  });
  document.addEventListener('pointerdown', event => {
    const target = event.target as Node;
    if (!popover.hidden && !popover.contains(target) && !button.contains(target)) setOpen(false);
  });

  el('fanList').addEventListener('change', event => {
    const box = event.target as HTMLInputElement;
    const key = box.dataset['fan'];
    if (!key) return;
    updatePrefs(p => {
      p.hiddenFans = box.checked ? p.hiddenFans.filter(k => k !== key) : [...new Set([...p.hiddenFans, key])];
    });
    renderFanList(getModel());
  });

  initLocalPrefs();
  void initServerSettings();

  return { refreshFans: () => { if (!popover.hidden) renderFanList(getModel()); } };
}
