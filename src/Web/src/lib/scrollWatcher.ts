/**
 * Application-wide scroll watcher:
 * Detects scroll events on any container (window, main, sidebar, tables, popovers)
 * using a passive capture listener.
 * Dynamically toggles 'is-scrolling' on the document body and scroll target,
 * smoothly revealing scrollbars during active scroll and hiding them when idle.
 */
export function initScrollWatcher(): void {
  if (typeof window === 'undefined') return;

  let timeoutId: number | null = null;
  const SCROLL_IDLE_DELAY_MS = 800;

  window.addEventListener(
    'scroll',
    (event) => {
      const target = event.target as HTMLElement | Document;
      document.body.classList.add('is-scrolling');

      if (target && target !== document && 'classList' in target) {
        target.classList.add('is-scrolling');
      }

      if (timeoutId !== null) {
        window.clearTimeout(timeoutId);
      }

      timeoutId = window.setTimeout(() => {
        document.body.classList.remove('is-scrolling');
        if (target && target !== document && 'classList' in target) {
          target.classList.remove('is-scrolling');
        }
        timeoutId = null;
      }, SCROLL_IDLE_DELAY_MS);
    },
    { capture: true, passive: true }
  );
}
