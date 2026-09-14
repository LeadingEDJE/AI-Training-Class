import { useCallback, useSyncExternalStore } from 'react';

/**
 * Tailwind's `md` breakpoint, in pixels.
 *
 * **The project's ONE layout breakpoint.** `FormGrid` in `components/ui.tsx` chose `md:` over the
 * mockups' `@media(max-width:900px)` and recorded why: introducing a second at 900px "creates a band
 * where this grid and the rest of the shell disagree about how wide the screen is". Compass defines no
 * custom `--breakpoint-*` token, so `md` is 768 and this constant must stay in step with the `md:`
 * utilities that share the decision.
 */
export const MD_BREAKPOINT_PX = 768;

/** One pixel below `md`, because `md:` is `min-width: 768px` — so 768 itself is NOT narrow. */
const NARROW_QUERY = `(max-width: ${MD_BREAKPOINT_PX - 1}px)`;

function mediaQuery(): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return null;
  }
  return window.matchMedia(NARROW_QUERY);
}

/**
 * Whether the viewport is narrower than Tailwind's `md` breakpoint.
 *
 * **Why a hook rather than `hidden md:block` on two rendered layouts.** `StackedRows` needs to render
 * *either* a table or cards, and the CSS approach renders BOTH and hides one. That is fine in a browser
 * — `display: none` drops the hidden branch from the accessibility tree and zeroes its metrics — but it
 * puts duplicate rows in the DOM, and **jsdom applies no CSS at all**, so every existing unit test on
 * those screens suddenly saw each row twice. Six specs broke on the first attempt. Fixing those six by
 * scoping their queries would have left the duplication for every future test and reader of these
 * screens to rediscover, which is the shape of trap this feature exists to remove.
 *
 * **`useSyncExternalStore`, not `useState` + `useEffect`.** The first version set state inside an effect
 * to re-read the query after mount, which `react-hooks/set-state-in-effect` flags — correctly: it
 * forces a second render, and it still leaves a gap between the render-time read and the listener being
 * attached. This is exactly the "subscribe to something outside React" case the hook exists for, so the
 * subscription and the read are one thing and the gap does not exist.
 *
 * **The SSR/jsdom snapshot is `false` (desktop), deliberately.** `matchMedia` is undefined in jsdom
 * unless a test shims it, so without the guard this would throw on import across the existing suite.
 * Falling back to the table keeps every current unit test meaningful: they assert table semantics, and a
 * test that silently started asserting card semantics instead would be a changed test wearing an
 * unchanged name. A test that wants the card layout shims `matchMedia` and says so.
 */
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

  // No `getServerSnapshot`: Compass renders with `createRoot`, never `hydrateRoot` (`src/main.tsx`),
  // so there is no server pass for React to reconcile against. Passing one would be speculative
  // (Principle II) and unreachable — it showed up as the only uncovered line in this file. If Compass
  // ever server-renders, add it back with a test that exercises it.
  return useSyncExternalStore(subscribe, getSnapshot);
}
