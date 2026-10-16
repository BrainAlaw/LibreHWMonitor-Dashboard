// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { Model } from '../model';
import { sensor, snapshot } from '../testing';
import { renderRaw } from './raw';

describe('raw motherboard temperatures', () => {
  it('retains unavailable sensors with their identity and a neutral state', () => {
    document.body.innerHTML = '<span id="rawCount"></span><table><tbody id="rawTable"></tbody></table>';
    renderRaw(new Model(snapshot([
      sensor({ id: '/lpc/nct6798d/0/temperature/0', name: 'Motherboard', type: 'Temperature', value: null })
    ])));
    const row = document.querySelector('#rawTable tr');
    expect(row?.textContent).toContain('Motherboard');
    expect(row?.querySelector('[data-label="Value"]')?.textContent).toBe('--');
    expect(row?.querySelector('[data-label="Health"]')?.textContent).toBe('Reading unavailable');
    expect(row?.querySelector('[data-label="Health"]')?.className).toBe('muted');
  });

  it('shows unidentified inputs and their extrema with a neutral explanation', () => {
    document.body.innerHTML = '<span id="rawCount"></span><table><tbody id="rawTable"></tbody></table>';
    const model = new Model(snapshot([
      sensor({
        id: '/lpc/nct6798d/0/temperature/2', name: 'Temperature #3', type: 'Temperature',
        value: 89.5, min: 88, max: 91, unit: '°C'
      }),
      sensor({ id: '/lpc/nct6798d/0/temperature/0', name: 'Motherboard', type: 'Temperature', value: 95, unit: '°C' })
    ]));

    renderRaw(model);

    const rows = document.querySelectorAll('#rawTable tr');
    expect(document.getElementById('rawCount')?.textContent).toBe('2 sensors');
    expect(rows[0]?.querySelector('[data-label="Min"]')?.textContent).toBe('88.0 °C');
    expect(rows[0]?.querySelector('[data-label="Value"]')?.textContent).toBe('89.5 °C');
    expect(rows[0]?.querySelector('[data-label="Max"]')?.textContent).toBe('91.0 °C');
    expect(rows[0]?.querySelector('[data-label="Health"]')?.textContent).toBe('Unidentified sensor; alerts disabled');
    expect(rows[0]?.querySelector('[data-label="Health"]')?.className).toBe('muted');
    expect(rows[1]?.querySelector('[data-label="Health"]')?.className).toBe('bad-text');
    expect(rows[1]?.querySelector('[data-label="Health"]')?.textContent).toBe('Critical temperature');
  });
});
