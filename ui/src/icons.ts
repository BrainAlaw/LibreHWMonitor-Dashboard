import { raw, type Safe } from './html';
import type { Sensor } from './types';

const ICONS: Record<string, string> = {
  cpu: '<path d="M7 7h10v10H7z"/><path d="M10 10h4v4h-4z"/><path d="M4 9h3M4 12h3M4 15h3M17 9h3M17 12h3M17 15h3M9 4v3M12 4v3M15 4v3M9 17v3M12 17v3M15 17v3"/>',
  gpu: '<path d="M4 7h13v10H4z"/><path d="M17 10h3v4h-3M7 10h4v4H7z"/><path d="M6 19h9"/>',
  memory: '<path d="M5 8h14v8H5z"/><path d="M7 6v2M10 6v2M13 6v2M16 6v2M7 16v2M10 16v2M13 16v2M16 16v2"/>',
  storage: '<ellipse cx="12" cy="6" rx="7" ry="3"/><path d="M5 6v8c0 1.7 3.1 3 7 3s7-1.3 7-3V6"/><path d="M5 10c0 1.7 3.1 3 7 3s7-1.3 7-3"/>',
  network: '<circle cx="12" cy="12" r="8"/><path d="M4 12h16M12 4c2 2.2 3 4.8 3 8s-1 5.8-3 8M12 4c-2 2.2-3 4.8-3 8s1 5.8 3 8"/>',
  power: '<path d="M13 2 5 13h6l-1 9 9-13h-6l1-7z"/>',
  fan: '<circle cx="12" cy="12" r="2"/><path d="M12 10c2.8-5.3 8.6-2.4 6.3 1.7-1.2 2-3.7 2.2-6.3.3M14 13c5.9.4 6.2 6.9 1.5 6.8-2.3-.1-3.8-2.2-3.5-5M10 13c-3.1 5-8.7 1.6-6-2.3 1.3-1.9 3.9-1.8 6 .3"/>',
  thermal: '<path d="M10 14.5V5a2 2 0 1 1 4 0v9.5a4 4 0 1 1-4 0z"/><path d="M12 8v7"/>',
  clock: '<circle cx="12" cy="12" r="8"/><path d="M12 7v5l3 2"/>',
  voltage: '<path d="M7 7h9l1 2h2v6h-2l-1 2H7z"/><path d="M10 11h4M10 14h2"/>',
  load: '<path d="M5 19V5M5 19h14"/><path d="M8 15l3-4 3 2 4-6"/>',
  generic: '<path d="M12 3 21 12 12 21 3 12z"/>'
};

const RULES: Array<[RegExp, string]> = [
  [/download|upload|network|pcie|throughput/, 'network'],
  [/wall|power|watt|package|board power|internal dc|peripheral/, 'power'],
  [/vram|gpu memory|memory controller|gpu|graphics|geforce|radeon|nvidia/, 'gpu'],
  [/cpu|processor|core load/, 'cpu'],
  [/ram|system memory|memory/, 'memory'],
  [/storage|disk|ssd|nvme|used space/, 'storage'],
  [/fan|cooling|pump/, 'fan'],
  [/temperature|thermal|hot spot|junction/, 'thermal'],
  [/clock|frequency/, 'clock'],
  [/voltage|volt/, 'voltage']
];

export function gaugeIcon(sensor: Sensor | undefined, label = ''): Safe {
  const haystack = [label, sensor?.displayName, sensor?.text, sensor?.deviceName, sensor?.category, sensor?.type]
    .filter(Boolean).join(' ').toLowerCase();
  let icon = RULES.find(([pattern]) => pattern.test(haystack))?.[1];
  if (!icon && (sensor?.type === 'Load' || /load|utilisation|utilization|used/.test(haystack))) icon = 'load';
  return raw(`<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[icon ?? 'generic']}</svg>`);
}
