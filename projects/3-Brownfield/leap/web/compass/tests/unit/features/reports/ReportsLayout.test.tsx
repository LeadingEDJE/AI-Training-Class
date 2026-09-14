import { render, screen, waitFor } from '@testing-library/react';
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { COMPASS_BASE_PATH, createCompassRouter } from '../../../../src/routes/router';

/**
 * The reports shell, driven through the real router.
 *
 * `ReportsLayout` reads `useRouterState` and renders `<Link>`s and an `<Outlet>`, none of which work
 * outside a router — so this mounts the actual route tree over `createMemoryHistory`, the seam
 * `router.ts` documents for exactly this. Testing it with a stubbed router context would prove the stub.
 */
function renderAt(pathname: string) {
  const router = createCompassRouter(
    createMemoryHistory({ initialEntries: [`${COMPASS_BASE_PATH}${pathname}`] }),
  );
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

const EMPTY_REPORT = {
  asOfDate: '2026-08-17',
  currentlyAvailable: [],
  confirmedRollouts: [],
  unconfirmedSows: [],
};

/**
 * The root route renders `CompassNav`, which reads `/api/me` and calls `getInitials` on the display
 * name — so a catch-all mock that answers every URL with the report body makes the NAV throw, the root
 * error boundary swallow the whole tree, and the failure surface as "cannot find the report" rather
 * than "the nav blew up". Route the two endpoints separately.
 */
const CURRENT_USER = {
  displayName: 'Kara Mitchell',
  email: 'kara.mitchell@example.test',
  roles: ['Compass Sales'],
  privileges: [],
};

describe('ReportsLayout', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    fetchSpy.mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('/api/me')
          ? { ok: true, status: 200, json: async () => CURRENT_USER }
          : { ok: true, status: 200, json: async () => EMPTY_REPORT },
      ),
    );
  });

  it('renders exactly four tabs, and no RPT-2 tab restored from the mockup', async () => {
    renderAt('/reports');

    const nav = await screen.findByRole('navigation', { name: 'Reports' });
    const tabs = await waitFor(() => {
      const links = nav.querySelectorAll('a');
      expect(links).toHaveLength(4);
      return links;
    });

    // The mockup's "SOWs Expiring in 90 Days" (RPT-2) is a report no acceptance criterion covers.
    // Principle II forbids building it and Principle X's last rule says a design source does not
    // authorise scope. T096 asserts this too — the departure must stay deliberate. "SOW Extension
    // Report" (issue #534) is a fourth tab answering a different question, requested directly rather
    // than drawn from the mockup — it is not RPT-2 restored.
    expect([...tabs].map((tab) => tab.textContent)).toEqual([
      'Availability Report',
      'Client Assignment Duration',
      'Assignment Start',
      'SOW Extension Report',
    ]);
    expect(screen.queryByText('SOWs Expiring in 90 Days')).not.toBeInTheDocument();
  });

  it('points each tab at its own address, so every report is deep-linkable', async () => {
    renderAt('/reports');

    const nav = await screen.findByRole('navigation', { name: 'Reports' });

    // Tabs are links, not local state (FR-023, Q3). A `useState` strip would give all four reports
    // one URL and break deep links, bookmarks, back and reload.
    expect([...nav.querySelectorAll('a')].map((tab) => tab.getAttribute('href'))).toEqual([
      `${COMPASS_BASE_PATH}/reports/availability`,
      `${COMPASS_BASE_PATH}/reports/assignment-duration`,
      `${COMPASS_BASE_PATH}/reports/assignment-start`,
      `${COMPASS_BASE_PATH}/reports/sow-extension`,
    ]);
  });

  it('renders the Availability Report at the bare /reports path, in place (FR-023)', async () => {
    renderAt('/reports');

    // Resolves rather than falling through to the not-found page, which is FR-023's requirement, and it
    // resolves to the default report rather than a chooser (owner request 2026-08-19).
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Availability Report' }),
    ).toBeInTheDocument();

    // The Availability Report tab IS marked current here (owner report 2026-08-19).
    //
    // This previously asserted ZERO current tabs, as the accepted cost of rendering in place. That
    // trade weighed only two options -- redirect, or prefix-match the active check -- and there is a
    // third: resolve the index pathname to the route the index actually renders, then keep comparing
    // exactly. Which is precisely what `nav-identity.ts` already does for `/compass/` rendering the Team
    // Directory, where the same unhighlighted front door was treated as a DEFECT ("gave a screen-reader
    // user no aria-current") rather than a cost. Two sibling navigations in one app answering the same
    // interaction differently was the worse outcome the router's own docstring warned about.
    //
    // Still no redirect: the address bar and the single-press Back are untouched, which is what that
    // warning was actually protecting.
    const current = screen
      .getByRole('navigation', { name: 'Reports' })
      .querySelectorAll('a[aria-current="page"]');

    // The COUNT, not just the first match: exact equality is retained specifically so a second tab
    // cannot also light up, and only asserting length catches that.
    expect(current).toHaveLength(1);
    expect(current[0].textContent).toBe('Availability Report');
  });

  it('does not light a tab on an unknown path under /reports', async () => {
    // The guard on the resolution above: only the bare index path resolves to the default report. A
    // sibling that merely starts the same way must not inherit the underline -- the defect a
    // prefix-matched active check would have introduced.
    renderAt('/reports/assignment-duration');

    const current = await waitFor(() => {
      const links = screen
        .getByRole('navigation', { name: 'Reports' })
        .querySelectorAll('a[aria-current="page"]');
      expect(links).toHaveLength(1);
      return links[0];
    });

    expect(current.textContent).toBe('Client Assignment Duration');
  });

  it.each([
    ['/reports/availability', 'Availability Report'],
    ['/reports/assignment-duration', 'Client Assignment Duration'],
    ['/reports/assignment-start', 'Assignment Start'],
    ['/reports/sow-extension', 'SOW Extension Report'],
  ])('marks the tab for %s as the current page', async (pathname, label) => {
    renderAt(pathname);

    // aria-current is what announces the active tab; these are navigation links, so the ARIA tab
    // pattern (which expects tabpanels) would be the wrong role to claim.
    const current = await waitFor(() => {
      // querySelectorALL, and the length asserted: with `querySelector` a second tab also carrying
      // aria-current="page" passes silently, which is exactly the bug a prefix-matched active check
      // produces. Two current pages is an accessibility defect, so the count is the assertion.
      const links = screen
        .getByRole('navigation', { name: 'Reports' })
        .querySelectorAll('a[aria-current="page"]');
      expect(links).toHaveLength(1);
      return links[0] as HTMLElement;
    });

    expect(current.textContent).toBe(label);
  });

  it('deep-links straight to a report, rendering it without a click', async () => {
    renderAt('/reports/availability');

    // The property `reports-availability.critical.spec.ts` also asserts in a real browser: arriving
    // directly at the tab's URL must render that report, not the index and not a 404.
    expect(await screen.findByText('No EDJErs are currently available.')).toBeInTheDocument();
  });

  it('offers no export control, and does not narrate that in the header (FR-022, AC-NFR-6)', async () => {
    renderAt('/reports/availability');

    expect(await screen.findByRole('heading', { name: 'Reports' })).toBeInTheDocument();

    // AC-NFR-6 requires that no export EXISTS, not that the screen says so. This used to assert the
    // header copy instead, which was the mockup's RPT-1 annotation ("on-screen only for MVP -- PDF
    // export is a future enhancement") mistaken for UI text; annotations are scaffolding and are not
    // reproduced (`ui.tsx` remarks; spec Finding 4 resolves RPT-1 as a note about AC-NFR-6, not a
    // screen element). So the assertion moves to the requirement rather than being deleted with the copy.
    expect(screen.queryByText(/no PDF, print or CSV export/i)).not.toBeInTheDocument();
    for (const label of [/export/i, /download/i, /print/i, /csv/i, /pdf/i]) {
      expect(screen.queryByRole('button', { name: label })).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: label })).not.toBeInTheDocument();
    }
  });
});
