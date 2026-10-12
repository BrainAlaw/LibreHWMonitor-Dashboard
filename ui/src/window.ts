/** Custom title bar for the desktop shell (WebView2 host). Plain browsers and LAN viewers keep their own frame. */
interface WebViewHost {
  postMessage(message: string): void;
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void;
}

const host = (window as unknown as { chrome?: { webview?: WebViewHost } }).chrome?.webview;

export function initWindowChrome(): void {
  if (!host) return;

  const root = document.documentElement;
  root.dataset['shell'] = 'app';

  host.addEventListener('message', event => {
    if (event.data === 'maximized' || event.data === 'restored') root.dataset['maximized'] = String(event.data === 'maximized');
  });

  const bar = document.querySelector<HTMLElement>('.topbar');
  const isInteractive = (target: EventTarget | null): boolean =>
    target instanceof Element && target.closest('button, a, input, select') !== null;

  bar?.addEventListener('pointerdown', event => {
    if (event.button === 0 && !isInteractive(event.target)) host.postMessage('drag');
  });
  bar?.addEventListener('dblclick', event => {
    if (!isInteractive(event.target)) host.postMessage('maximize');
  });

  document.querySelectorAll<HTMLElement>('[data-window]').forEach(button => {
    button.addEventListener('click', () => host.postMessage(button.dataset['window'] ?? ''));
  });
}
