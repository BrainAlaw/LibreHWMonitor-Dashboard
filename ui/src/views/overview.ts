import { signal, thermalTile, cpuThermalTile } from '../components';
import { setHtml, setText, el } from '../dom';
import { h } from '../html';
import type { Model } from '../model';
import { bestCpuTemperature, chooseStorageTemperature, isLiveTemperature, systemLevel } from '../scoring';
import type { Sensor } from '../types';
import { groupBy, deviceRoot } from './util';

const BADGE: Record<string, { label: string; className: string }> = {
  critical: { label: 'Critical', className: 'badge bad-text' },
  warn: { label: 'Watch', className: 'badge warn-text' },
  ok: { label: 'Nominal', className: 'badge ok' }
};

export function renderOverview(model: Model): void {
  renderThermals(model);
  renderRiskBadge(model);
  renderLoads(model);
}

function renderThermals(model: Model): void {
  const cpuTemp = bestCpuTemperature(model);
  const gpuTemp = model.byText('GPU Core', 'gpu', 'Temperature');
  const gpuHotspot = model.byText('GPU Hot Spot', 'gpu', 'Temperature');
  const gpuJunction = model.byText('GPU Memory Junction', 'gpu', 'Temperature');
  const storage = groupBy(model.sensors.filter(s => s.category === 'storage' && isLiveTemperature(s)), deviceRoot)
    .map(([name, sensors]) => thermalTile(chooseStorageTemperature(sensors), name, 'NVMe/SSD composite'));

  setHtml('thermalSummary', h`<div class="signal-grid">
    ${cpuThermalTile(cpuTemp)}
    ${thermalTile(gpuTemp, 'GPU Core', gpuTemp?.deviceName)}
    ${thermalTile(gpuHotspot, 'GPU Hot Spot', gpuHotspot?.deviceName)}
    ${thermalTile(gpuJunction, 'GPU Memory', gpuJunction?.deviceName)}
    ${storage}
  </div>`);
}

function renderRiskBadge(model: Model): void {
  const { label, className } = BADGE[systemLevel(model)] ?? BADGE['ok']!;
  setText('systemRisk', label);
  el('systemRisk').className = className;
}

function renderLoads(model: Model): void {
  const byGpu = (text: string, type = 'Load'): Sensor | undefined => model.byText(text, 'gpu', type);
  const vramUsed = byGpu('GPU Memory Used', 'SmallData');
  const vramTotal = byGpu('GPU Memory Total', 'SmallData');
  const vramPct = vramUsed?.number != null && vramTotal?.number ? Math.min(100, (vramUsed.number / vramTotal.number) * 100) : 0;
  const vramSub = `${vramUsed?.valueText || '--'} / ${vramTotal?.valueText || '--'}`;
  const gpuPackagePower = byGpu('GPU Package', 'Power');

  setHtml('loadSummary', h`<div class="signal-grid">
    ${signal(model.byText('CPU Total', 'cpu', 'Load'), 'CPU Total', 'processor load')}
    ${signal(byGpu('GPU Core'), 'GPU Core', 'graphics load')}
    ${signal(byGpu('GPU Memory'), 'GPU Memory', 'vram load')}
    ${signal(byGpu('GPU Memory Controller'), 'GPU Memory Ctrl', 'controller load')}
    ${signal(model.byText('Memory', 'memory', 'Load'), 'System RAM', model.byText('Memory Used', 'memory', 'Data')?.valueText)}
    ${signal(vramUsed, 'VRAM Used', vramSub, vramPct)}
    ${signal(byGpu('GPU Power'), 'GPU Power', gpuPackagePower?.valueText || 'board load')}
  </div>`);
}
