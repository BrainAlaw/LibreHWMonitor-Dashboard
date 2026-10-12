import { el, setHtml } from './dom';
import { connectionIssue } from './header';
import { h } from './html';
import type { Model } from './model';
import { bindPopover } from './popover';
import { systemIssues } from './scoring';
import type { Level } from './types';

interface Row {
  title: string;
  detail: string;
  reason: string;
  level: Level;
}

const LEVEL_LABEL: Record<Level, string> = { ok: 'Nominal', warn: 'Watch', critical: 'Critical' };

function collect(model: Model | null): Row[] {
  const rows: Row[] = [];
  const connection = connectionIssue();
  if (connection) rows.push({ title: connection.title, detail: connection.detail, reason: '', level: connection.level });
  if (model) {
    for (const { title, detail, risk } of systemIssues(model)) {
      rows.push({ title, detail, reason: risk.reason, level: risk.level });
    }
  }
  return rows;
}

function worst(rows: Row[]): Level {
  if (rows.some(row => row.level === 'critical')) return 'critical';
  return rows.some(row => row.level === 'warn') ? 'warn' : 'ok';
}

/** Wires the problems button and its popover; call the returned refresh after every data change. */
export function initIssues(getModel: () => Model | null): { refresh(): void } {
  const button = el('systemRisk');

  const render = (): void => {
    const rows = collect(getModel());
    const level = worst(rows);
    const label = rows.length === 0 ? 'System status: nominal' : `System status: ${LEVEL_LABEL[level].toLowerCase()} (${rows.length})`;
    button.dataset['level'] = level;
    button.title = label;
    button.setAttribute('aria-label', label);
    el('systemRiskCount').textContent = rows.length > 0 ? String(Math.min(rows.length, 99)) : '';

    if (el('issuesPopover').hidden) return;
    el('issuesSummary').textContent = rows.length === 0
      ? LEVEL_LABEL.ok
      : `${LEVEL_LABEL[level]} - ${rows.length} ${rows.length === 1 ? 'issue' : 'issues'}`;
    setHtml('issuesList', rows.length === 0
      ? h`<p class="settings-note">All monitored sensors are within their normal range.</p>`
      : h`${rows.map(row => h`<div class="issue" data-level="${row.level}">
          <span class="issue-title">${row.title}</span>
          <span class="issue-detail">${row.reason ? `${row.detail} · ${row.reason}` : row.detail}</span>
        </div>`)}`);
  };

  bindPopover(button, el('issuesPopover'), render);
  return { refresh: render };
}
