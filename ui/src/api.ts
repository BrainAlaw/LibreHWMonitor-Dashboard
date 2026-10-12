import type { Health, SettingsView, Snapshot } from './types';

export interface StreamHandlers {
  onSnapshot(snapshot: Snapshot): void;
  onHealth(health: Health): void;
  onDisconnect(): void;
}

/** Subscribes to the server-sent event stream; EventSource reconnects on its own. */
export function connectStream(handlers: StreamHandlers): EventSource {
  const source = new EventSource(new URL('api/stream', window.location.href));

  source.addEventListener('snapshot', event => handlers.onSnapshot(JSON.parse((event as MessageEvent<string>).data) as Snapshot));
  source.addEventListener('health', event => handlers.onHealth(JSON.parse((event as MessageEvent<string>).data) as Health));
  source.addEventListener('error', () => handlers.onDisconnect());
  return source;
}

export async function getSettings(): Promise<SettingsView> {
  const response = await fetch(new URL('api/settings', window.location.href), { cache: 'no-store' });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return (await response.json()) as SettingsView;
}

export interface SaveResult {
  ok: boolean;
  restartRequired: boolean;
  firewallOk: boolean;
  error?: string;
}

export interface SettingsUpdate {
  port: number;
  samplingMs: number;
  autostart: boolean;
  startMinimized: boolean;
  closeToTray: boolean;
  allowLan: boolean;
  regenerateToken?: boolean;
}

export async function saveSettings(update: SettingsUpdate): Promise<SaveResult> {
  const response = await fetch(new URL('api/settings', window.location.href), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(update)
  });
  const body = (await response.json()) as { restartRequired?: boolean; firewallOk?: boolean; error?: string };
  return { ok: response.ok, restartRequired: body.restartRequired === true, firewallOk: body.firewallOk !== false, error: body.error };
}
