import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { MD_BREAKPOINT_PX, useIsNarrowViewport } from '../../../src/hooks/useIsNarrowViewport';

/**
 * Feature 011 (issue #83). The hook `StackedRows` uses to render EITHER a table or cards.
 *
 * **Why the default matters more than it looks.** jsdom has no `matchMedia`, and the first attempt at
 * `StackedRows` used `hidden md:block` on two rendered layouts instead of this hook — which put
 * duplicate rows in the DOM and broke six existing report specs, because jsdom applies no CSS and so
 * saw both branches. The desktop default here is what keeps those specs asserting table semantics
 * without being rewritten.
 */

/** Installs a controllable `matchMedia`. Returns a setter that fires the change listeners. */
function stubMatchMedia(initialMatches: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>();
  let matches = initialMatches;

  const query = {
    get matches() {
      return matches;
    },
    addEventListener: (_: 'change', listener: (event: MediaQueryListEvent) => void) => {
      listeners.add(listener);
    },
    removeEventListener: (_: 'change', listener: (event: MediaQueryListEvent) => void) => {
      listeners.delete(listener);
    },
  };

  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => query),
  );

  return {
    set(next: boolean) {
      matches = next;
      for (const listener of listeners) {
        listener({ matches: next } as MediaQueryListEvent);
      }
    },
    listenerCount: () => listeners.size,
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('useIsNarrowViewport', () => {
  it("pins the breakpoint to Tailwind md, the project's one layout breakpoint", () => {
    // Must stay in step with the `md:` utilities that share the decision — `FormGrid`'s docstring
    // records why 768 and not the mockups' 900.
    expect(MD_BREAKPOINT_PX).toBe(768);
  });

  it('reports false when matchMedia is unavailable, so jsdom gets the table', () => {
    // No stub installed: this is the state every existing unit test runs in. A throw here, or a `true`,
    // would silently switch those specs to asserting card semantics under unchanged names.
    vi.stubGlobal('matchMedia', undefined);

    const { result } = renderHook(() => useIsNarrowViewport());

    expect(result.current).toBe(false);
  });

  it('reports true when the viewport is narrower than md', () => {
    stubMatchMedia(true);

    const { result } = renderHook(() => useIsNarrowViewport());

    expect(result.current).toBe(true);
  });

  it('reports false when the viewport is at or above md', () => {
    stubMatchMedia(false);

    const { result } = renderHook(() => useIsNarrowViewport());

    expect(result.current).toBe(false);
  });

  it('follows a resize across the breakpoint in both directions', () => {
    const media = stubMatchMedia(false);
    const { result } = renderHook(() => useIsNarrowViewport());

    expect(result.current).toBe(false);

    act(() => media.set(true));
    expect(result.current, 'a narrowing resize switches to cards').toBe(true);

    act(() => media.set(false));
    expect(result.current, 'and widening switches back to the table').toBe(false);
  });

  it('removes its listener on unmount, so a resize cannot update a gone component', () => {
    const media = stubMatchMedia(false);
    const { unmount } = renderHook(() => useIsNarrowViewport());

    expect(media.listenerCount()).toBe(1);
    unmount();
    expect(media.listenerCount()).toBe(0);
  });

  it('queries one pixel below the breakpoint, so 768 itself is NOT narrow', () => {
    stubMatchMedia(false);
    renderHook(() => useIsNarrowViewport());

    // `md:` is min-width 768, so 768 must render the table and 767 the cards. Both are named viewports
    // in the NFR catalog, which is what makes that boundary directly testable in the e2e suite.
    expect(window.matchMedia).toHaveBeenCalledWith(`(max-width: ${MD_BREAKPOINT_PX - 1}px)`);
  });
});
