import { cpuThermalTile, numericTile, signal } from '../components';
import { setHtml, setText } from '../dom';
import { clamp } from '../format';
import { h } from '../html';
import type { Model } from '../model';
import { bestCpuPackagePower, bestCpuTemperature, colorForPercent } from '../scoring';

export function renderCpu(model: Model): void {
  setText('cpuName', model.cpuName());

  const load = model.byText('CPU Total', 'cpu', 'Load');
  const power = bestCpuPackagePower(model);
  const voltage = model.byContains(['Core', 'SVI2'], 'cpu', 'Voltage');

  setHtml('cpuSummary', h`<div class="signal-grid compact">
    ${signal(load, 'CPU Total', 'processor utilisation')}
    ${cpuThermalTile(bestCpuTemperature(model))}
    ${numericTile(power, 'CPU Package', power?.deviceName, 'POWER')}
    ${numericTile(voltage, 'Core Voltage', voltage?.deviceName, 'VOLT')}
  </div>`);

  const cores = model.sensors
    .filter(s => s.category === 'cpu' && s.type === 'Load' && /CPU Core #/i.test(s.text))
    .sort((a, b) => a.text.localeCompare(b.text, undefined, { numeric: true }));

  setHtml('coreGrid', h`${cores.map((core, index) => {
    const color = colorForPercent(core.number);
    const coreNumber = core.text.match(/#(\d+)/)?.[1] ?? String(index + 1);
    return h`<div class="core" style="--core-load:${clamp(core.number ?? 0, 0, 100)}%;--core-color:${color};border-color:${color}" title="${core.pathText}">
      <div class="core-fill"></div>
      <div class="core-index">Core ${coreNumber}</div>
      <div class="core-load">${core.valueText || '--'}</div>
    </div>`;
  })}`);
}
