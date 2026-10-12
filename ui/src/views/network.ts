import { setHtml } from '../dom';
import { formatRate } from '../format';
import { h, type Safe } from '../html';
import type { History } from '../history';
import { kilobytesPerSecond, type Model } from '../model';
import type { Sensor } from '../types';
import { groupBy, deviceRoot } from './util';

const CHART_WIDTH = 320;
const CHART_HEIGHT = 60;

function linePath(values: readonly number[], maxValue: number): string {
  if (!values.length) return '';
  const points = values.length === 1 ? [0, values[0] ?? 0] : values;
  return points.map((value, index) => {
    const x = (index / (points.length - 1)) * CHART_WIDTH;
    const y = CHART_HEIGHT - (value / maxValue) * (CHART_HEIGHT - 10) - 5;
    return `${index === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
  }).join(' ');
}

function areaPath(line: string): string {
  return line ? `${line} L ${CHART_WIDTH} ${CHART_HEIGHT} L 0 ${CHART_HEIGHT} Z` : '';
}

function trafficChart(download: readonly number[], upload: readonly number[]): Safe {
  const maxValue = Math.max(...download, ...upload, 1);
  const down = linePath(download, maxValue);
  const up = linePath(upload, maxValue);
  return h`<div class="traffic-chart">
    <svg viewBox="0 0 ${CHART_WIDTH} ${CHART_HEIGHT}" preserveAspectRatio="none" role="img" aria-label="Network traffic history">
      <path class="down" d="${areaPath(down)}" opacity=".62"></path>
      <path class="up" d="${areaPath(up)}" opacity=".58"></path>
      <path class="down" d="${down}" fill="none" stroke-width="2.6"></path>
      <path class="up" d="${up}" fill="none" stroke-width="2.2"></path>
    </svg>
  </div>`;
}

function totalSpeed(groups: Array<[string, Sensor[]]>, pattern: RegExp): number {
  return groups.reduce((sum, [, sensors]) => sum + kilobytesPerSecond(sensors.find(s => pattern.test(s.text))), 0);
}

export function renderNetwork(model: Model, history: History): void {
  const groups = groupBy(model.sensors.filter(s => s.category === 'network'), deviceRoot);
  const download = totalSpeed(groups, /download speed/i);
  const upload = totalSpeed(groups, /upload speed/i);
  history.push('download', download);
  history.push('upload', upload);

  const downloadHistory = history.get('download');
  const uploadHistory = history.get('upload');

  setHtml('networkSummary', h`<article class="network-card">
    <div class="mini-grid">
      <div class="metric-tile">
        <div class="metric-title"><strong>Download</strong><span class="metric-type">PEAK ${formatRate(Math.max(...downloadHistory, 1))}</span></div>
        <div class="metric-value" style="color:var(--blue)">${formatRate(download)}</div>
      </div>
      <div class="metric-tile">
        <div class="metric-title"><strong>Upload</strong><span class="metric-type">PEAK ${formatRate(Math.max(...uploadHistory, 1))}</span></div>
        <div class="metric-value" style="color:var(--cyan)">${formatRate(upload)}</div>
      </div>
    </div>
    ${trafficChart(downloadHistory, uploadHistory)}
  </article>`);
}

