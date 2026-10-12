import './styles.css';
import { connectStream } from './api';
import { setText } from './dom';
import { hideFan } from './fans';
import { History } from './history';
import { Model, signatureOf } from './model';
import { initSettingsPanel, showDisconnected, showHealth } from './sidebar';
import type { Snapshot } from './types';
import { renderCpu } from './views/cpu';
import { renderGpu } from './views/gpu';
import { renderNetwork } from './views/network';
import { renderOverview } from './views/overview';
import { renderPower } from './views/power';
import { renderRaw } from './views/raw';

const history = new History();
let model: Model | null = null;
let lastSignature = '';

function render(current: Model): void {
  setText('hostName', current.host || 'PC Monitor');
  renderOverview(current);
  renderCpu(current);
  renderGpu(current);
  renderNetwork(current, history);
  renderPower(current);
  renderRaw(current);
}

function onSnapshot(snapshot: Snapshot): void {
  // The stream pushes every sample; skip the DOM work when nothing changed.
  const signature = signatureOf(snapshot);
  if (signature === lastSignature) return;
  lastSignature = signature;
  model = new Model(snapshot);
  render(model);
}

document.addEventListener('click', event => {
  const button = (event.target as Element).closest<HTMLElement>('.fan-hide');
  if (!button?.dataset['key']) return;
  hideFan(button.dataset['key']);
  if (model) renderPower(model);
});

connectStream({ onSnapshot, onHealth: showHealth, onDisconnect: showDisconnected });
void initSettingsPanel();
