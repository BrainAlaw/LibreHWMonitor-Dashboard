// @vitest-environment happy-dom
import { beforeEach, describe, expect, it } from 'vitest';
import { morph } from './morph';

let root: HTMLElement;

beforeEach(() => {
  document.body.innerHTML = '<div id="root"></div>';
  root = document.getElementById('root') as HTMLElement;
});

describe('morph', () => {
  it('renders into an empty element', () => {
    morph(root, '<p>hello</p>');
    expect(root.innerHTML).toBe('<p>hello</p>');
  });

  it('keeps existing elements alive when only text and attributes change', () => {
    morph(root, '<div class="a" style="--x:1">one</div>');
    const original = root.firstElementChild;

    morph(root, '<div class="b" style="--x:2">two</div>');

    expect(root.firstElementChild).toBe(original);
    expect(original?.className).toBe('b');
    expect(original?.getAttribute('style')).toBe('--x:2');
    expect(original?.textContent).toBe('two');
  });

  it('removes attributes and surplus children', () => {
    morph(root, '<ul><li class="x">1</li><li>2</li><li>3</li></ul>');
    morph(root, '<ul><li>1</li></ul>');

    expect(root.innerHTML).toBe('<ul><li>1</li></ul>');
  });

  it('adds missing children', () => {
    morph(root, '<ul><li>1</li></ul>');
    morph(root, '<ul><li>1</li><li>2</li></ul>');

    expect(root.querySelectorAll('li')).toHaveLength(2);
  });

  it('replaces elements whose tag changed', () => {
    morph(root, '<p>text</p>');
    morph(root, '<span>text</span>');

    expect(root.innerHTML).toBe('<span>text</span>');
  });

  it('matches keyed children by key so their elements survive reordering', () => {
    morph(root, '<i data-key="a">A</i><i data-key="b">B</i><i data-key="c">C</i>');
    const [a, , c] = Array.from(root.children);

    morph(root, '<i data-key="c">C</i><i data-key="a">A</i>');

    expect(Array.from(root.children)).toEqual([c, a]);
  });

  it('inserts new keyed children without disturbing others', () => {
    morph(root, '<i data-key="a">A</i>');
    const a = root.firstElementChild;

    morph(root, '<i data-key="z">Z</i><i data-key="a">A</i>');

    expect(root.children[1]).toBe(a);
    expect(root.children[0]?.textContent).toBe('Z');
  });
});
