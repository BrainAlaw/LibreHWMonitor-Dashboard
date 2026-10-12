/** Marks a string as already-safe HTML so the `h` tag does not escape it again. */
export class Safe {
  constructor(readonly html: string) {}
}

const ESCAPES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;'
};

export function esc(value: unknown): string {
  return String(value ?? '').replace(/[&<>"']/g, ch => ESCAPES[ch] ?? ch);
}

export function raw(html: string): Safe {
  return new Safe(html);
}

function renderValue(value: unknown): string {
  if (value instanceof Safe) return value.html;
  if (Array.isArray(value)) return value.map(renderValue).join('');
  if (value === null || value === undefined || value === false) return '';
  return esc(value);
}

/** Tagged template that escapes every interpolation unless it is `Safe` (or an array of them). */
export function h(strings: TemplateStringsArray, ...values: unknown[]): Safe {
  let out = strings[0] ?? '';
  values.forEach((value, index) => {
    out += renderValue(value) + (strings[index + 1] ?? '');
  });
  return new Safe(out);
}
