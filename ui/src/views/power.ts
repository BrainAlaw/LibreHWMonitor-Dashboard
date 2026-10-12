import { coolingCard, valueSignal } from '../components';
import { setHtml, setText } from '../dom';
import { h } from '../html';
import { activeCoolingSensors, fanKey } from '../fans';
import type { Model } from '../model';
import { hardwareKind } from '../scoring';
import { groupBy, deviceRoot } from './util';

/** Rough assumptions for everything the sensors cannot measure. */
export const POWER_ASSUMPTIONS = {
  motherboardWatts: 45,
  ramWatts: 12,
  fansAndCoolingWatts: 8,
  usbWatts: 15,
  monitorCount: 2,
  monitorWatts: 32,
  nvmeWatts: 5,
  sataSsdWatts: 2,
  psuEfficiency: 0.88
} as const;

export interface PowerEstimate {
  measuredDc: number;
  internalDc: number;
  peripherals: number;
  wall: number;
}

export function estimatePower(model: Model): PowerEstimate {
  const cpuMeasured = model.byText('Package', 'cpu', 'Power')?.number ?? 0;
  const gpuMeasured = model.byText('GPU Package', 'gpu', 'Power')?.number ?? 0;
  const cpuLoad = model.byText('CPU Total', 'cpu', 'Load')?.number ?? 0;
  const gpuLoad = (model.byText('GPU Board Power', 'gpu', 'Load') ?? model.byText('GPU Power', 'gpu', 'Load'))?.number ?? 0;

  const storage = groupBy(model.sensors.filter(s => s.category === 'storage'), deviceRoot)
    .map(([, sensors]) => (sensors[0] ? hardwareKind(sensors[0]) : ''));
  const nvmeCount = storage.filter(kind => kind === 'NVMe').length;
  const sataCount = storage.filter(kind => kind === 'SSD').length;

  const cpu = cpuMeasured > 0 ? cpuMeasured : 35 + (cpuLoad / 100) * 80;
  const gpu = gpuMeasured > 0 ? gpuMeasured : 25 + (gpuLoad / 100) * 320;
  const storageWatts = nvmeCount * POWER_ASSUMPTIONS.nvmeWatts + sataCount * POWER_ASSUMPTIONS.sataSsdWatts;
  const platform = POWER_ASSUMPTIONS.motherboardWatts + POWER_ASSUMPTIONS.ramWatts + POWER_ASSUMPTIONS.fansAndCoolingWatts;
  const peripherals = POWER_ASSUMPTIONS.usbWatts + POWER_ASSUMPTIONS.monitorCount * POWER_ASSUMPTIONS.monitorWatts;
  const internalDc = cpu + gpu + storageWatts + platform;

  return {
    measuredDc: cpuMeasured + gpuMeasured,
    internalDc,
    peripherals,
    wall: internalDc / POWER_ASSUMPTIONS.psuEfficiency + peripherals
  };
}

export function renderPower(model: Model): void {
  const estimate = estimatePower(model);
  setHtml('powerSummary', h`
    ${valueSignal('Wall Estimate', `${Math.round(estimate.wall)} W`, 'PC + monitors + USB, approximate', Math.min(100, estimate.wall / 7), 'var(--orange)')}
    ${valueSignal('Measured CPU/GPU', `${Math.round(estimate.measuredDc)} W`, 'from hardware power sensors', Math.min(100, estimate.measuredDc / 5), 'var(--green)')}
    ${valueSignal('Internal DC', `${Math.round(estimate.internalDc)} W`, 'cpu + gpu + board + ram + storage', Math.min(100, estimate.internalDc / 6), 'var(--blue)')}
    ${valueSignal('Peripherals', `${Math.round(estimate.peripherals)} W`, `${POWER_ASSUMPTIONS.monitorCount} monitors + USB estimate`, Math.min(100, estimate.peripherals), 'var(--cyan)')}`);

  const fans = activeCoolingSensors(model);
  setText('powerFanCount', fans.length ? `${fans.length} fans` : 'loose model');
  setHtml('powerCoolingGrid', fans.length
    ? h`${fans.map((fan, index) => coolingCard(fan, fanKey(fan), index))}`
    : h`<div class="muted">No active fan or pump telemetry reported.</div>`);
}
