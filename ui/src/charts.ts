import { h, type Safe } from './html';

const WIDTH = 320;
const HEIGHT = 100;

export interface Series {
  values: readonly number[];
  color: string;
  label: string;
}

function path(values: readonly number[], maxValue: number): string {
  if (values.length === 0) return '';
  const points = values.length === 1 ? [0, values[0] ?? 0] : values;
  return points.map((value, index) => {
    const x = (index / (points.length - 1)) * WIDTH;
    const y = HEIGHT - 6 - (value / maxValue) * (HEIGHT - 12);
    return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`;
  }).join(' ');
}

/**
 * Multi-series area chart. All series share one scale: either `fixedMax` (e.g. 100 for percentages)
 * or the largest value seen.
 */
export function areaChart(series: Series[], fixedMax?: number): Safe {
  const maxValue = fixedMax ?? Math.max(1, ...series.flatMap(s => s.values));
  const paths = series.map(s => {
    const line = path(s.values, maxValue);
    const area = line ? `${line} L${WIDTH} ${HEIGHT} L0 ${HEIGHT} Z` : '';
    return h`<path class="area" d="${area}" fill="${s.color}"></path><path class="line" d="${line}" stroke="${s.color}"></path>`;
  });

  return h`<div class="chart"><svg viewBox="0 0 ${WIDTH} ${HEIGHT}" preserveAspectRatio="none" role="img" aria-label="History chart">${paths}</svg></div>`;
}

export function legend(items: Array<{ label: string; color: string; value: string }>): Safe {
  return h`<div class="legend">${items.map(item => h`<span style="--c:${item.color}">${item.label} <b>${item.value}</b></span>`)}</div>`;
}
