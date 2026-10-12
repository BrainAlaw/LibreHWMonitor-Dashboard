import type { Safe } from './html';
import { morph } from './morph';

export function el<T extends HTMLElement = HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing element #${id}`);
  return element as T;
}

export function setText(id: string, value: string): void {
  const element = el(id);
  if (element.textContent !== value) element.textContent = value;
}

/** Patches the element's content in place (see `morph`), keeping animations alive. */
export function setHtml(id: string, content: Safe): void {
  morph(el(id), content.html);
}
