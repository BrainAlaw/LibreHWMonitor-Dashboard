import { el } from './dom';

export type ToastKind = 'info' | 'ok' | 'warn' | 'error';

/** Shows a short message in the bottom-right corner. */
export function toast(message: string, kind: ToastKind = 'info', durationMs = 3600): void {
  const item = document.createElement('div');
  item.className = `toast ${kind}`;
  item.textContent = message;
  el('toasts').appendChild(item);

  window.setTimeout(() => {
    item.classList.add('leaving');
    window.setTimeout(() => item.remove(), 320);
  }, durationMs);
}
