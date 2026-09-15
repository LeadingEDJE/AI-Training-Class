import { useCallback, useSyncExternalStore } from 'react';

/** Tailwind's `md` breakpoint, in pixels, matching the mockup's own `@media(max-width:900px)` query. */
export const MD_BREAKPOINT_PX = 768;

const NARROW_QUERY = `(max-width: ${MD_BREAKPOINT_PX - 1}px)`;

function mediaQuery(): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return null;
  }
  return window.matchMedia(NARROW_QUERY);
}

/** Whether the viewport is narrower than Tailwind's `md` breakpoint, tracked via `useState` and `useEffect`. */
export function useIsNarrowViewport(): boolean {
  const subscribe = useCallback((onStoreChange: () => void) => {
    const query = mediaQuery();
    if (query === null) {
      return () => undefined;
    }

    query.addEventListener('change', onStoreChange);
    return () => query.removeEventListener('change', onStoreChange);
  }, []);

  const getSnapshot = useCallback(() => mediaQuery()?.matches ?? false, []);

  return useSyncExternalStore(subscribe, getSnapshot);
}
