import { setHtml, setText } from '../dom';
import { h } from '../html';
import type { Model } from '../model';
import { hardwareKind, scoreSensor } from '../scoring';

const RISK_CLASS = { critical: 'bad-text', warn: 'warn-text', ok: 'ok' } as const;

export function renderRaw(model: Model): void {
  setText('rawCount', `${model.sensors.length} sensors`);
  setHtml('rawTable', h`${model.sensors.map(sensor => {
    const risk = scoreSensor(sensor);
    return h`<tr data-key="${sensor.id}">
      <td data-label="Device"><strong>${sensor.deviceName}</strong><div class="path">${hardwareKind(sensor)}</div></td>
      <td data-label="Sensor"><strong>${sensor.displayName}</strong><div class="path">${sensor.id}</div></td>
      <td data-label="Type"><span class="badge">${sensor.type}</span></td>
      <td data-label="Min" class="numeric">${sensor.minText || '--'}</td>
      <td data-label="Value" class="numeric">${sensor.valueText || '--'}</td>
      <td data-label="Max" class="numeric">${sensor.maxText || '--'}</td>
      <td data-label="Health" class="${RISK_CLASS[risk.level]}">${risk.reason}</td>
    </tr>`;
  })}`);
}
