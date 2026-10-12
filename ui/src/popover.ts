/** Anchored popovers: one open at a time, closed by Escape or a click outside. */
let active: { close(): void } | null = null;

export interface PopoverHandle {
  isOpen(): boolean;
  close(): void;
}

export function bindPopover(button: HTMLElement, popover: HTMLElement, onOpen?: () => void): PopoverHandle {
  const handle: PopoverHandle = {
    isOpen: () => !popover.hidden,
    close: () => setOpen(false)
  };

  function setOpen(open: boolean): void {
    if (open && active && active !== handle) active.close();
    popover.hidden = !open;
    button.setAttribute('aria-expanded', String(open));
    if (open) {
      active = handle;
      // Align the popover's right edge with its button.
      popover.style.right = `${Math.max(8, window.innerWidth - button.getBoundingClientRect().right)}px`;
      onOpen?.();
    } else if (active === handle) {
      active = null;
    }
  }

  button.addEventListener('click', () => setOpen(Boolean(popover.hidden)));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') setOpen(false);
  });
  document.addEventListener('pointerdown', event => {
    const target = event.target as Node;
    if (!popover.hidden && !popover.contains(target) && !button.contains(target)) setOpen(false);
  });

  return handle;
}
