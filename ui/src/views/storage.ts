import { setHtml, setText } from '../dom';
import { clamp, formatRate } from '../format';
import { h } from '../html';
import { kilobytesPerSecond, type Model } from '../model';
import { chooseStorageTemperature, hardwareKind, tempColor } from '../scoring';
import type { Sensor } from '../types';
import { deviceRoot, groupBy } from './util';

function usedColor(percent: number): string {
  if (percent >= 95) return 'var(--red)';
  if (percent >= 85) return 'var(--yellow)';
  return 'var(--green)';
}

function find(sensors: Sensor[], type: string, pattern: RegExp): Sensor | undefined {
  return sensors.find(s => s.type === type && pattern.test(s.text));
}

export function renderStorage(model: Model): void {
  const drives = groupBy(model.sensors.filter(s => s.category === 'storage'), deviceRoot)
    .sort(([a], [b]) => a.localeCompare(b, undefined, { numeric: true }));

  setText('storageCount', drives.length ? `${drives.length} drives` : '');
  setHtml('driveGrid', drives.length === 0
    ? h`<div class="muted">No storage telemetry reported.</div>`
    : h`${drives.map(([name, sensors]) => {
      const temp = chooseStorageTemperature(sensors);
      const used = find(sensors, 'Load', /used space/i);
      const life = find(sensors, 'Level', /remaining life|available spare/i);
      const read = find(sensors, 'Throughput', /read rate/i);
      const write = find(sensors, 'Throughput', /write rate/i);
      const usedPct = clamp(used?.number ?? 0, 0, 100);

      return h`<article class="drive-card" data-key="${name}">
        <div class="drive-head">
          <div>
            <div class="drive-name" title="${name}">${name}</div>
            <div class="drive-kind">${hardwareKind(sensors[0] as Sensor)}${life ? ` · health ${life.valueText}` : ''}</div>
          </div>
          <div class="drive-temp" style="color:${tempColor(temp?.number ?? null)}">${temp?.valueText || '--'}</div>
        </div>
        <div class="bar" title="Used space"><i style="--w:${usedPct}%;--bar-color:${usedColor(usedPct)}"></i></div>
        <div class="drive-stats">
          <span>used <b>${used?.valueText || '--'}</b></span>
          <span>R <b>${formatRate(kilobytesPerSecond(read))}</b></span>
          <span>W <b>${formatRate(kilobytesPerSecond(write))}</b></span>
        </div>
      </article>`;
    })}`);
}
