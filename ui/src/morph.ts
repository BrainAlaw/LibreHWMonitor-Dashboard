/**
 * Minimal DOM patcher: updates `target` in place so it matches `html`, instead of replacing its children.
 * Keeping the same elements alive lets CSS transitions and animations (spinning fans, moving dials) continue
 * across the one-second refresh. Children with a `data-key` are matched by key, others by position.
 */
export function morph(target: Element, html: string): void {
  const template = document.createElement('template');
  template.innerHTML = html;
  syncChildren(target, template.content);
}

function keyOf(node: Node): string | null {
  return node instanceof Element ? node.getAttribute('data-key') : null;
}

function sameKind(a: Node, b: Node): boolean {
  if (a.nodeType !== b.nodeType) return false;
  if (a instanceof Element && b instanceof Element) return a.localName === b.localName && keyOf(a) === keyOf(b);
  return true;
}

function syncChildren(target: Node, source: Node): void {
  const wanted = Array.from(source.childNodes);

  wanted.forEach((next, index) => {
    let current = target.childNodes[index] ?? null;
    const key = keyOf(next);

    if (key !== null && (current === null || keyOf(current) !== key)) {
      const match = Array.from(target.childNodes).slice(index + 1).find(child => keyOf(child) === key);
      if (match) {
        target.insertBefore(match, current);
        current = match;
      }
    }

    if (current === null) {
      target.appendChild(next.cloneNode(true));
    } else if (key !== null && keyOf(current) !== null && keyOf(current) !== key) {
      // A new keyed child goes in front of an existing keyed one instead of replacing (and destroying) it.
      target.insertBefore(next.cloneNode(true), current);
    } else if (!sameKind(current, next)) {
      target.replaceChild(next.cloneNode(true), current);
    } else {
      patchNode(current, next);
    }
  });

  while (target.childNodes.length > wanted.length) {
    target.removeChild(target.lastChild as Node);
  }
}

function patchNode(current: Node, next: Node): void {
  if (!(current instanceof Element) || !(next instanceof Element)) {
    if (current.nodeValue !== next.nodeValue) current.nodeValue = next.nodeValue;
    return;
  }

  for (const attribute of Array.from(current.attributes)) {
    if (!next.hasAttribute(attribute.name)) current.removeAttribute(attribute.name);
  }
  for (const attribute of Array.from(next.attributes)) {
    if (current.getAttribute(attribute.name) !== attribute.value) current.setAttribute(attribute.name, attribute.value);
  }
  if (current instanceof HTMLInputElement && next instanceof HTMLInputElement && current.value !== next.value) {
    current.value = next.value;
  }

  syncChildren(current, next);
}
