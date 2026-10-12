import type { Safe } from './html';

export function el<T extends HTMLElement = HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing element #${id}`);
  return element as T;
}

export function setText(id: string, value: string): void {
  el(id).textContent = value;
}

export function setHtml(id: string, content: Safe): void {
  el(id).innerHTML = content.html;
}
