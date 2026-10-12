import './styles.css';
import { connectStream } from './api';
import { el } from './dom';
import { showDisconnected, showHealth } from './header';
import { History } from './history';
import { Model, signatureOf } from './model';
import { onPrefsChange, prefs } from './prefs';
import { initSettings } from './settings';
import type { Snapshot } from './types';
import { renderCpu } from './views/cpu';
import { renderGpu } from './views/gpu';
import { recordNetworkHistory, renderNetwork } from './views/network';
import { recordLoadHistory, renderOverview, showIssueToasts } from './views/overview';
import { renderPower } from './views/power';
import { renderRaw } from './views/raw';
import { renderStorage } from './views/storage';

const history = new History();
let model: Model | null = null;
let lastSignature = '';

function render(current: Model): void {
  const { showRaw } = prefs();
  el('rawSection').hidden = !showRaw;

  renderOverview(current, history);
  renderCpu(current);
  renderGpu(current);
  renderNetwork(current, history);
  renderPower(current);
  renderStorage(current);
  if (showRaw) renderRaw(current);
}

const settings = initSettings(() => model);

function onSnapshot(snapshot: Snapshot): void {
  // The stream pushes every sample; skip the DOM work when nothing changed.
  const signature = signatureOf(snapshot);
  if (signature === lastSignature) return;
  lastSignature = signature;

  model = new Model(snapshot);
  recordLoadHistory(model, history);
  recordNetworkHistory(model, history);
  render(model);
  settings.refreshFans();
}

el('systemRisk').addEventListener('click', () => {
  if (model) showIssueToasts(model);
});

onPrefsChange(() => {
  if (model) render(model);
});

connectStream({ onSnapshot, onHealth: showHealth, onDisconnect: showDisconnected });
