import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

/**
 * The shared `.tabs` strip both Compass shells navigate with.
 *
 * <h3>Why the router is stubbed here, when `ReportsLayout.test.tsx` mounts the real one</h3>
 * What this component DOES is a pure function of one input: pathname in, which item is current out.
 * Driving that through a real route tree means one test per real route and no way at all to reach the
 * trailing-slash case, which no route produces. So the pathname is injected directly.
 *
 * The real-router half is not skipped, it is elsewhere: `ReportsLayout.test.tsx` mounts the actual tree
 * over `createMemoryHistory`, and `compass-lookups.critical.spec.ts` asserts the admin strip's
 * `aria-current` in a real browser. This file covers the logic; those cover the wiring.
 */
/**
 * The pathname the mocked router reports. Written by {@link renderAt} on every render.
 *
 * No initial value and no `beforeEach` reset: both were there and both were dead, because every test
 * goes through `renderAt` and `renderAt` assigns before rendering. A reset that cannot run reads as an
 * isolation guarantee and provides none — a test rendering `TabNav` directly would inherit the previous
 * test's pathname and the `beforeEach` would not have saved it, since it runs before that render rather
 * than after. Leaving it unset makes `renderAt` the only writer, and a direct render throws on
 * `undefined` instead of silently picking up stale state.
 */
const pathnameRef: { current?: string } = {};

vi.mock('@tanstack/react-router', () => ({
  useRouterState: ({ select }: { select: (state: unknown) => unknown }) => {
    if (pathnameRef.current === undefined) {
      throw new Error('render through renderAt() — it sets the pathname the strip reads');
    }
    return select({ location: { pathname: pathnameRef.current } });
  },
  // Spreads the rest, so `aria-current` and `className` reach the DOM and can be asserted. A stub that
  // rendered only `to` and `children` would make every assertion below about the stub.
  Link: ({ to, children, ...rest }: { to: string; children: ReactNode }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
}));

const { TabNav } = await import('../../../src/components/TabNav');

const ITEMS = [
  { to: '/admin/lookups', label: 'Lookups' },
  { to: '/admin/edjers', label: 'EDJErs' },
  { to: '/admin/clients', label: 'Clients' },
];

function renderAt(pathname: string, items = ITEMS) {
  pathnameRef.current = pathname;
  return render(
    <TabNav
      label="Configuration areas"
      items={items}
      indexPath="/admin"
      indexRenders="/admin/lookups"
    />,
  );
}

/** The one link the strip reports as current, or `null` when it reports none. */
function currentTab(): HTMLElement | null {
  const current = screen
    .getAllByRole('link')
    .filter((link) => link.getAttribute('aria-current') === 'page');
  expect(current.length, 'exactly one tab may be current').toBeLessThan(2);
  return current[0] ?? null;
}

describe('TabNav', () => {
  it('offers a link to every destination, inside a named landmark', () => {
    renderAt('/admin/lookups');

    const nav = screen.getByRole('navigation', { name: 'Configuration areas' });
    expect(nav).toBeInTheDocument();
    for (const item of ITEMS) {
      expect(screen.getByRole('link', { name: item.label })).toHaveAttribute('href', item.to);
    }
  });

  it('marks the destination matching the current path, and only that one', () => {
    renderAt('/admin/edjers');

    expect(currentTab()).toHaveTextContent('EDJErs');
  });

  it('marks the index path as the destination it actually renders', () => {
    // `/compass/admin` renders Lookup Administration without redirecting to `/admin/lookups`, so an
    // exact-match check finds nothing and the strip highlights nothing while a screen is on display.
    // This is the substitution that fixes it — the gap `router.ts` recorded as accepted until now.
    renderAt('/admin');

    expect(currentTab()).toHaveTextContent('Lookups');
  });

  it('treats a trailing slash as the same address', () => {
    // TanStack hands back whichever form the caller navigated to. Unreachable through a real route
    // tree, which is one reason the pathname is injected here.
    renderAt('/admin/edjers/');

    expect(currentTab()).toHaveTextContent('EDJErs');
  });

  it('marks the index path as current even with a trailing slash', () => {
    // The two normalisations have to compose: strip the slash FIRST, then substitute. Reversed, `/admin/`
    // never equals `indexPath` and this lands on the no-current-tab branch.
    renderAt('/admin/');

    expect(currentTab()).toHaveTextContent('Lookups');
  });

  it('marks nothing current on a path no destination owns', () => {
    // A CHILD of a destination is the case that matters: `/admin/edjers/new` is a real route today.
    // A `startsWith` active check would light the EDJErs tab here — which is defensible — but it would
    // equally light BOTH tabs for any path extending two of them, and announce two current pages. The
    // rule is exact equality plus named substitutions, so this reports none.
    renderAt('/admin/edjers/new');

    expect(currentTab()).toBeNull();
  });

  it('renders its landmark even with no destinations to offer', () => {
    // Unconditional so the layout does not shift as areas are added.
    renderAt('/admin', []);

    expect(screen.getByRole('navigation', { name: 'Configuration areas' })).toBeInTheDocument();
    expect(screen.queryAllByRole('link')).toHaveLength(0);
  });

  it('styles the current destination differently from the rest', () => {
    // The mockup's `.tabs div.on`: the EDJE green underline and a semibold label. Asserted because the
    // active state has to be visible as well as announced — `aria-current` alone leaves a sighted
    // viewer with no indication at all.
    renderAt('/admin/edjers');

    const active = screen.getByRole('link', { name: 'EDJErs' });
    const inactive = screen.getByRole('link', { name: 'Lookups' });

    expect(active.className).toContain('border-brand-green');
    expect(active.className).toContain('font-semibold');
    expect(inactive.className).toContain('border-transparent');
    expect(inactive.className).not.toContain('border-brand-green');
  });
});
