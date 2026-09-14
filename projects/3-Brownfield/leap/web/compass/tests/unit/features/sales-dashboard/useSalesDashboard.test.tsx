import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import { tableLinkClass } from '../../../../src/components/ui-classes';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { SalesDashboardRoute } from '../../../../src/features/sales-dashboard/SalesDashboardRoute';

const DASHBOARD_RESPONSE = {
  asOfDate: '2026-07-22',
  activeSowCount: 68,
  expiringSowCount: 7,
  confirmedRolloutCount: 4,
  beachCount: 5,
};

const BREAKDOWN_RESPONSE = [
  {
    employeeId: 1,
    employeeName: 'Devon Brooks',
    clients: [{ id: 42, name: 'Scarlet Logistics' }],
    date: '2026-08-14',
    daysUntil: 23,
    coachName: 'Priya Natarajan',
  },
];

describe('SalesDashboardRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => BREAKDOWN_RESPONSE });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
  });

  function renderRoute() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={queryClient}>
        <SalesDashboardRoute />
      </QueryClientProvider>,
    );
  }

  it('requests the dashboard counts', async () => {
    renderRoute();

    await waitFor(() =>
      expect(fetchSpy.mock.calls.some((c) => String(c[0]).includes('/api/compass/dashboard'))).toBe(
        true,
      ),
    );
  });

  it('renders all four tiles with their counts and qualifying labels (FR-001, FR-028)', async () => {
    renderRoute();

    expect(await screen.findByText('68')).toBeInTheDocument();
    // Tiles are real <button>s, styled to the mockup's whole-tile click target — not headings,
    // matching the mockup's own markup, which has no heading element on a tile either.
    expect(
      screen.getByRole('button', { name: '68 Active Client Assignments' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: '7 SOWs Expiring < 90 Days (no follow-on SOW)' }),
    ).toBeInTheDocument();
    // Deviation 9: the mockup's "(future end date)" qualifier is replaced because it states AC-36's
    // literal rule, which FR-004's refinement overrides.
    expect(screen.getByRole('button', { name: /Confirmed Rollouts/ })).toBeInTheDocument();
    expect(screen.queryByText(/future end date/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '5 EDJErs on the Beach' })).toBeInTheDocument();
  });

  it('does not show an as-of date in the header (FR-027, issue #343)', async () => {
    renderRoute();

    // Wait for real data to land before asserting an absence — otherwise the loading state's
    // equally-absent text would make this pass without proving anything.
    expect(await screen.findByText('68')).toBeInTheDocument();
    expect(screen.queryByText(/as of/i)).not.toBeInTheDocument();
  });

  it('opens on the expiring-SOWs breakdown, not an empty panel (FR-028)', async () => {
    renderRoute();

    await waitFor(() =>
      expect(
        fetchSpy.mock.calls.some((c) => String(c[0]).includes('/breakdown/expiring-sows')),
      ).toBe(true),
    );
    expect(await screen.findByText('Devon Brooks')).toBeInTheDocument();
  });

  it('swaps the breakdown when a different tile is selected, showing only one at a time (FR-007)', async () => {
    const user = userEvent.setup();
    renderRoute();
    await screen.findByText('Devon Brooks');
    fetchSpy.mockClear();

    await user.click(screen.getByRole('button', { name: /Beach/ }));

    await waitFor(() =>
      expect(fetchSpy.mock.calls.some((c) => String(c[0]).includes('/breakdown/beach'))).toBe(true),
    );
    // Exactly one breakdown table, never two — the panel replaces rather than appends.
    expect(screen.getAllByRole('table')).toHaveLength(1);
  });

  it('tells a refused viewer they lack permission, NOT that the dashboard is broken', async () => {
    // CompassReporting admits Sales, Ops and the Compass root but not Compass Admin (FR-019). The
    // nav hides this screen from everyone it excludes; a bookmark or a shared link does not, so the
    // refused state is reachable. `edjer-api.ts` records the same reasoning: a generic error here
    // sends the viewer to diagnose an outage that is not happening.
    fetchSpy.mockResolvedValue({ ok: false, status: 403, json: async () => ({}) });
    renderRoute();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/do not have permission/i);
    expect(alert).not.toHaveTextContent(/could not be loaded/i);
  });

  it('does not retry a refusal', async () => {
    // A 403 is a verdict, not a blip: retrying it delays the permission message and writes identical
    // denials to the server's logs.
    //
    // Rendered with react-query's REAL retry defaults, not the harness's `retry: false`. That is what
    // makes this a regression guard rather than a tautology — under `retry: false` it would pass no
    // matter what the hook did. The property it pins is structural: `read` RESOLVES a non-OK status
    // instead of throwing, so react-query sees a success and never retries. Make 403 throw again and
    // this goes to four calls.
    fetchSpy.mockResolvedValue({ ok: false, status: 403, json: async () => ({}) });
    render(
      <QueryClientProvider client={new QueryClient()}>
        <SalesDashboardRoute />
      </QueryClientProvider>,
    );
    await screen.findByRole('alert');

    const dashboardCalls = fetchSpy.mock.calls.filter(
      (call) => !String(call[0]).includes('/breakdown/'),
    );
    expect(dashboardCalls).toHaveLength(1);
  });

  it('treats an expired session as refused rather than broken', async () => {
    // apiFetch drives the re-login overlay off the 401 itself. Rendering "could not be loaded"
    // underneath it would contradict the prompt the viewer is already looking at.
    fetchSpy.mockResolvedValue({ ok: false, status: 401, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toHaveTextContent(/do not have permission/i);
  });

  it('names EVERY client on a row, not just the first', async () => {
    // A confirmed-rollout or beach row is one per EDJEr, and an EDJEr can hold two assignments tied
    // on the date that keys the category. Naming one and dropping the other hides real work.
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: async () => [
            {
              employeeId: 9,
              employeeName: 'Rosa Iglesias',
              clients: [
                { id: 3, name: 'Alpha Internal' },
                { id: 8, name: 'Zulu Internal' },
              ],
              date: '2026-06-01',
              daysUntil: null,
              coachName: null,
            },
          ],
        });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
    renderRoute();

    const alpha = await screen.findByRole('link', { name: 'Alpha Internal' });
    const zulu = screen.getByRole('link', { name: 'Zulu Internal' });
    expect(alpha).toHaveAttribute('href', '/compass/client-directory/3');
    expect(zulu).toHaveAttribute('href', '/compass/client-directory/8');
    // Owner request 2026-08-21. The furthest from the baseline: no colour of its own (inherited from
    // the `td`) and an underline only on hover, so at rest it did not read as a link.
    expect(alpha.className).toBe(tableLinkClass);
    // One row, not two — the tile above counts distinct EDJErs (SC-003).
    expect(screen.getAllByRole('row')).toHaveLength(2); // header + one data row
  });

  it('surfaces a failed dashboard request as an error state', async () => {
    fetchSpy.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toHaveTextContent(/dashboard could not be loaded/i);
  });

  it('surfaces a failed breakdown request as its own error state, alongside a loaded dashboard', async () => {
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
    renderRoute();

    expect(await screen.findByRole('alert')).toHaveTextContent(/breakdown could not be loaded/i);
    // The tiles still render — one surface failing does not take down the other.
    expect(
      screen.getByRole('button', { name: '68 Active Client Assignments' }),
    ).toBeInTheDocument();
  });

  it('renders a no-coach row with every nullable field empty, rather than omitting the row (FR-008)', async () => {
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: async () => [
            {
              employeeId: 98,
              employeeName: 'Priyanka Solberg',
              clients: [],
              date: null,
              daysUntil: null,
              coachName: null,
            },
          ],
        });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
    renderRoute();

    expect(await screen.findByText('Priyanka Solberg')).toBeInTheDocument();
  });

  it('marks the selected tile with aria-pressed, and only that one', async () => {
    renderRoute();
    await screen.findByText('Devon Brooks');

    expect(screen.getByRole('button', { name: /SOWs Expiring/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: '68 Active Client Assignments' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );

    await userEvent.setup().click(screen.getByRole('button', { name: /Beach/ }));

    expect(screen.getByRole('button', { name: /Beach/ })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: /SOWs Expiring/ })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('does not show the former SD-1 footnote for any breakdown, expiring-SOWs included (issue #465)', async () => {
    renderRoute();
    await screen.findByText('Devon Brooks');

    expect(
      screen.queryByText(/only sows with no subsequent sow\/contract/i),
    ).not.toBeInTheDocument();

    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: '68 Active Client Assignments' }));
    await waitFor(() =>
      expect(fetchSpy.mock.calls.some((c) => String(c[0]).includes('/breakdown/active-sows'))).toBe(
        true,
      ),
    );

    expect(
      screen.queryByText(/only sows with no subsequent sow\/contract/i),
    ).not.toBeInTheDocument();
  });

  it('renders the days-until value as an urgency pill, coloured but never colour-only (SD-2)', async () => {
    renderRoute();

    // Devon Brooks' row carries daysUntil: 23, which the visible pill text still shows as "23" —
    // the number itself carries the information; colour is an accent, never the only signal.
    expect(await screen.findByText('23')).toBeInTheDocument();
  });

  it('gives a further-out days-until value the "later" pill tone, not the urgent one', async () => {
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: async () => [
            {
              employeeId: 3,
              employeeName: 'Maya Alvarez',
              clients: [{ id: 7, name: 'Buckeye Mutual' }],
              date: '2026-09-30',
              daysUntil: 70,
              coachName: 'Jordan Wells',
            },
          ],
        });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
    renderRoute();

    const pill = await screen.findByText('70');
    expect(pill.className).toContain('brand-taupe');
  });

  it('renders the client name as a link to the client directory page, not plain text', async () => {
    renderRoute();

    const link = await screen.findByRole('link', { name: 'Scarlet Logistics' });
    expect(link).toHaveAttribute('href', '/compass/client-directory/42');
  });

  it('renders a breakdown row with no client as plain text, not a broken link', async () => {
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: async () => [
            {
              employeeId: 98,
              employeeName: 'Priyanka Solberg',
              clients: [],
              date: null,
              daysUntil: null,
              coachName: null,
            },
          ],
        });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
    renderRoute();

    await screen.findByText('Priyanka Solberg');
    // Scoped to the CLIENT link. Issue #330 made the EDJEr's own name a link, so "no links anywhere
    // on the page" stopped expressing what this test is about — and would now fail on the very link
    // that issue added.
    const clientLinks = screen
      .queryAllByRole('link')
      .filter((link) => link.getAttribute('href')?.startsWith('/compass/client-directory/'));
    expect(clientLinks).toHaveLength(0);
  });

  it('gives every dashboard tile the pointer cursor on hover', async () => {
    renderRoute();

    expect(await screen.findByRole('button', { name: '68 Active Client Assignments' })).toHaveClass(
      'cursor-pointer',
    );
  });

  it('shows an explicit empty state for a category with no rows, rather than a blank panel', async () => {
    fetchSpy.mockImplementation((url: string) => {
      if (url.includes('/breakdown/')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => [] });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => DASHBOARD_RESPONSE });
    });
    renderRoute();

    expect(await screen.findByText(/no rows in this category/i)).toBeInTheDocument();
  });
});
