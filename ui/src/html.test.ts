import { describe, expect, it } from 'vitest';
import { esc, h, raw } from './html';

describe('h template tag', () => {
  it('escapes interpolated values', () => {
    const name = '<img src=x onerror="alert(1)">';
    expect(h`<b>${name}</b>`.html).toBe('<b>&lt;img src=x onerror=&quot;alert(1)&quot;&gt;</b>');
  });

  it('keeps nested templates and raw markup intact', () => {
    const inner = h`<i>${'a&b'}</i>`;
    expect(h`<p>${inner}${raw('<br>')}</p>`.html).toBe('<p><i>a&amp;b</i><br></p>');
  });

  it('joins arrays and drops null, undefined and false', () => {
    expect(h`${[h`<li>${1}</li>`, h`<li>${2}</li>`]}`.html).toBe('<li>1</li><li>2</li>');
    expect(h`[${null}${undefined}${false}]`.html).toBe('[]');
  });
});

describe('esc', () => {
  it('escapes quotes and ampersands', () => {
    expect(esc(`"it's" & <x>`)).toBe('&quot;it&#39;s&quot; &amp; &lt;x&gt;');
  });
});
