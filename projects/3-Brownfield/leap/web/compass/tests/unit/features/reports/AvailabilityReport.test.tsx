import { render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { AvailabilityReportRoute } from '../../../../src/features/reports/availability/AvailabilityReportRoute';

const FULL_REPORT = {
  asOfDate: '2026-08-17',
  currentlyAvailable: [
    {
      employeeId: 11,
      employeeName: 'Dana Reed',
      internalAssignmentStartDate: '2026-07-01',
      daysAvailable: 47,
      coachName: 'Priya Natarajan',
    },
    // No coach — must still render, with an empty coach cell (FR-013).
    {
      employeeId: 12,
      employeeName: 'Pat Nolan',
      internalAssignmentStartDate: '2026-07-15',
      daysAvailable: 33,
      coachName: null,
    },
  ],
  confirmedRollouts: [
    {
      employeeId: 21,
      employeeName: 'Ivy Stone',
      employeeType: 'Full Time',
      // Two clients: the tie on the latest end date. Both must be named.
      clients: [
        { id: 5, name: 'Nordhaven' },
        { id: 6, name: 'Quarry Ridge' },
      ],
      assignmentEndDate: '2026-09-30',
      daysUntilRollout: 44,
      coachName: null,
    },
  ],
  unconfirmedSows: [
    {
      sowId: 301,
      employeeId: 31,
      employeeName: 'Ken Diaz',
      employeeType: '1099',
      clients: [{ id: 7, name: 'Foxfield Energy' }],
      sowEndDate: '2026-10-01',
      daysUntilExpiration: 45,
      coachName: null,
    },
    // Same EDJEr twice: this section is one row per SOW (FR-003's unit), so employeeId is not a
    // unique React key here. A duplicate-key warning is a real defect — US1's T039 found exactly
    // this against live seeded data on the dashboard.
    {
      sowId: 302,
      employeeId: 31,
      employeeName: 'Ken Diaz',
      employeeType: '1099',
      clients: [{ id: 8, name: 'Buckeye Mutual' }],
      sowEndDate: '2026-10-20',
      daysUntilExpiration: 64,
      coachName: 'Priya Natarajan',
    },
  ],
};

const EMPTY_REPORT = {
  asOfDate: '2026-08-17',
  currentlyAvailable: [],
  confirmedRollouts: [],
  unconfirmedSows: [],
};

function renderRoute() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AvailabilityReportRoute />
    </QueryClientProvider>,
  );
}

function respond(body: unknown, { ok = true, status = 200 } = {}) {
  fetchSpy.mockResolvedValue({ ok, status, json: async () => body });
}

describe('AvailabilityReportRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
  });

  it('reads the availability endpoint through apiFetch', async () => {
    respond(FULL_REPORT);
    renderRoute();

    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith('/api/compass/reports/availability'));
  });

  it('renders the three sections in order, labelled as the mockup labels them', async () => {
    respond(FULL_REPORT);
    renderRoute();

    // h3, not h2. RPT-3 is ONE card with an h2 naming the report and three h3 subheadings inside it, so
    // the sections are parts of a report rather than three sibling reports.
    const headings = await screen.findAllByRole('heading', { level: 3 });

    expect(headings.map((heading) => heading.textContent)).toEqual([
      '1 · Currently Available EDJErs',
      '2 · Confirmed Rollouts',
      '3 · Unconfirmed SOWs (expiring in next 90 days)',
    ]);
  });

  it('names the report itself, so the three sections read as parts of one thing', async () => {
    respond(FULL_REPORT);
    renderRoute();

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Availability Report' }),
    ).toBeInTheDocument();
  });

  it('colour-codes each subheading, using the AA-safe step rather than the mockup value', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const headings = await screen.findAllByRole('heading', { level: 3 });

    // The classes, because the raw mockup colours all fail AA at the 13px it sets (2.63:1, 2.92:1,
    // 3.45:1) and the ratios themselves are asserted in brand-contrast.test.ts. This pins that the
    // screen uses the darkened tokens and keeps the colour-coding, not that it drops one for the other.
    expect(headings[0].className).toContain('text-brand-green-accent');
    expect(headings[1].className).toContain('text-brand-blue-accent');
    expect(headings[2].className).toContain('text-brand-teal-heading');
    for (const heading of headings) {
      expect(heading.className).toContain('text-[13px]');
    }
  });

  it('gives section 1 four columns, with no EDJE-client column', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const table = await screen.findByRole('table', { name: 'Currently available EDJErs' });
    const headers = within(table).getAllByRole('columnheader');

    // FR-029/T050: the mockup's `EDJE Client Assignment Start` is one DATE column, not a client
    // column plus a date — that misread is not what `# of Days Available` is (issue #334, spec
    // Deviation 11), so four here is correct rather than a relapse of the old over-build.
    expect(headers.map((header) => header.textContent)).toEqual([
      'EDJEr',
      'EDJE Client Assignment Start',
      '# of Days Available',
      'Coach',
    ]);
  });

  it('shows how many days each EDJEr has been available, right after their assignment start date', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const table = await screen.findByRole('table', { name: 'Currently available EDJErs' });
    const row = within(table).getByText('Dana Reed').closest('tr') as HTMLElement;
    const cells = within(row).getAllByRole('cell');

    expect(cells[2].textContent).toBe('47');
  });

  it('shows the employee type as its own column in the Confirmed Rollouts and Unconfirmed SOWs sections (issue #386)', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const rollouts = await screen.findByRole('table', { name: 'Confirmed rollouts' });
    const rolloutRow = within(rollouts).getByText('Ivy Stone').closest('tr') as HTMLElement;
    const rolloutCells = within(rolloutRow).getAllByRole('cell');
    // EDJEr, Employee Type, Client, Assignment End Date, # Days Until Rollout, Coach.
    expect(rolloutCells[1].textContent).toBe('Full Time');

    const sows = screen.getByRole('table', { name: 'Unconfirmed SOWs expiring within 90 days' });
    const sowRows = within(sows).getAllByText('Ken Diaz');
    for (const nameCell of sowRows) {
      const row = nameCell.closest('tr') as HTMLElement;
      expect(within(row).getAllByRole('cell')[1].textContent).toBe('1099');
    }
  });

  it('does not show an employee-type column in the Currently Available section', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const available = await screen.findByRole('table', { name: 'Currently available EDJErs' });
    expect(
      within(available).queryByRole('columnheader', { name: 'Employee Type' }),
    ).not.toBeInTheDocument();
    expect(within(available).queryByText('Full Time')).not.toBeInTheDocument();
  });

  it('gives sections 2 and 3 six columns each, with Employee Type following EDJEr (issue #386)', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const rollouts = await screen.findByRole('table', { name: 'Confirmed rollouts' });
    expect(
      within(rollouts)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual([
      'EDJEr',
      'Employee Type',
      'Current Client',
      'Assignment End Date',
      '# Days Until Rollout',
      'Coach',
    ]);

    const sows = await screen.findByRole('table', {
      name: 'Unconfirmed SOWs expiring within 90 days',
    });
    expect(
      within(sows)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual([
      'EDJEr',
      'Employee Type',
      'Current Client',
      'Current SOW End',
      '# Days Until SOW Expiration',
      'Coach',
    ]);
  });

  it('renders every date as mm/dd/yyyy through formatDate', async () => {
    respond(FULL_REPORT);
    renderRoute();

    // issue #234's repo-wide mandate. The ISO string must never reach the screen.
    expect(await screen.findByText('07/01/2026')).toBeInTheDocument();
    expect(screen.getByText('09/30/2026')).toBeInTheDocument();
    expect(screen.getByText('10/01/2026')).toBeInTheDocument();
    expect(screen.queryByText('2026-07-01')).not.toBeInTheDocument();
  });

  it('keeps a no-coach EDJEr in all three sections, with an empty coach cell', async () => {
    respond(FULL_REPORT);
    renderRoute();

    // The compensating control J19 names: a coachless EDJEr must be VISIBLE here, because no coach
    // was notified for them. A filtered row loses the control silently.
    expect(await screen.findByText('Pat Nolan')).toBeInTheDocument();
    expect(screen.getByText('Ivy Stone')).toBeInTheDocument();
    expect(screen.getAllByText('Ken Diaz')).toHaveLength(2);

    const available = screen.getByRole('table', { name: 'Currently available EDJErs' });
    const noCoachRow = within(available).getByText('Pat Nolan').closest('tr');
    expect(noCoachRow).not.toBeNull();
    // Coach is now the FOURTH cell (index 3): EDJEr, assignment start, # of days available, coach.
    expect(within(noCoachRow as HTMLElement).getAllByRole('cell')[3].textContent).toBe('');
  });

  it('names every client on a row that ties on the latest end date', async () => {
    respond(FULL_REPORT);
    renderRoute();

    // Naming one and dropping the other would hide a client from a sales screen.
    expect(await screen.findByText('Nordhaven, Quarry Ridge')).toBeInTheDocument();
  });

  it('renders one row per SOW, so an EDJEr with two exposed SOWs appears twice', async () => {
    respond(FULL_REPORT);
    renderRoute();

    const sows = await screen.findByRole('table', {
      name: 'Unconfirmed SOWs expiring within 90 days',
    });

    expect(within(sows).getAllByRole('row')).toHaveLength(3); // header + two SOWs
  });

  // Each section's empty state is asserted INDEPENDENTLY of the others, because that is the case the
  // per-section requirement exists for: one empty section beside two populated ones must say so
  // rather than leave a gap that reads as a failed fetch (spec Edge Cases).
  it.each([
    ['currentlyAvailable', 'No EDJErs are currently available.'],
    ['confirmedRollouts', 'No confirmed rollouts.'],
    ['unconfirmedSows', 'No unconfirmed SOWs are expiring in the next 90 days.'],
  ])('shows %s’s own empty state while the other sections have rows', async (section, message) => {
    respond({ ...FULL_REPORT, [section]: [] });
    renderRoute();

    expect(await screen.findByText(message)).toBeInTheDocument();
  });

  it('shows all three empty states when nothing qualifies', async () => {
    respond(EMPTY_REPORT);
    renderRoute();

    expect(await screen.findByText('No EDJErs are currently available.')).toBeInTheDocument();
    expect(screen.getByText('No confirmed rollouts.')).toBeInTheDocument();
    expect(
      screen.getByText('No unconfirmed SOWs are expiring in the next 90 days.'),
    ).toBeInTheDocument();
  });

  it('renders an empty cell rather than "null" where a date or day count is absent', async () => {
    // Reachable data, not a defensive nicety: a beach assignment can have no start date recorded, and
    // §2's date comes from a nullable EndDate column. Rendering the raw null would put "null" on a
    // sales screen; a `??` that defaulted to 0 days would claim the EDJEr rolls off today.
    respond({
      ...FULL_REPORT,
      currentlyAvailable: [
        {
          employeeId: 41,
          employeeName: 'No Start Date',
          internalAssignmentStartDate: null,
          coachName: null,
        },
      ],
      confirmedRollouts: [
        {
          employeeId: 42,
          employeeName: 'No End Date',
          employeeType: null,
          clients: [{ id: 9, name: 'Granville Retail' }],
          assignmentEndDate: null,
          daysUntilRollout: null,
          coachName: null,
        },
      ],
      // §3 used to coalesce a null date to the business date and a null count to 0, which rendered as
      // "expires today / 0 days" -- the most urgent row the screen can show -- for data it did not
      // have. Both must now be empty cells.
      unconfirmedSows: [
        {
          // Also null, so the index-key fallback is exercised rather than left as an uncovered branch.
          sowId: null,
          employeeId: 43,
          employeeName: 'No Sow End',
          employeeType: null,
          clients: [{ id: 10, name: 'Kestrel Aviation' }],
          sowEndDate: null,
          daysUntilExpiration: null,
          coachName: null,
        },
      ],
    });
    renderRoute();

    const available = await screen.findByRole('table', { name: 'Currently available EDJErs' });
    const availableRow = within(available).getByText('No Start Date').closest('tr');
    expect(within(availableRow as HTMLElement).getAllByRole('cell')[1].textContent).toBe('');

    // Indices shift by one from the pre-#386 layout: EDJEr(0), Employee Type(1), Client(2),
    // Assignment End Date(3), # Days Until Rollout(4), Coach(5).
    const rollouts = screen.getByRole('table', { name: 'Confirmed rollouts' });
    const rolloutRow = within(rollouts).getByText('No End Date').closest('tr');
    const rolloutCells = within(rolloutRow as HTMLElement).getAllByRole('cell');
    expect(rolloutCells[1].textContent).toBe('');
    expect(rolloutCells[3].textContent).toBe('');
    expect(rolloutCells[4].textContent).toBe('');

    const sows = screen.getByRole('table', { name: 'Unconfirmed SOWs expiring within 90 days' });
    const sowCells = within(
      within(sows).getByText('No Sow End').closest('tr') as HTMLElement,
    ).getAllByRole('cell');
    expect(sowCells[1].textContent).toBe('');
    expect(sowCells[3].textContent).toBe('');
    expect(sowCells[4].textContent).toBe('');

    // Specifically NOT today's date and NOT "0": the old coalescing produced both.
    expect(sowCells[4].textContent).not.toBe('0');
    expect(screen.queryByText('null')).not.toBeInTheDocument();
  });

  it('tells a refused viewer they lack permission, not that the screen is broken', async () => {
    // Reachable by deep link or bookmark even though the nav does not offer it (FR-019). "Could not
    // be loaded" would report a working screen as broken.
    respond(null, { ok: false, status: 403 });
    renderRoute();

    expect(
      await screen.findByText('You do not have permission to view the Availability Report.'),
    ).toBeInTheDocument();
  });

  it('reports a genuine failure as a failure', async () => {
    respond(null, { ok: false, status: 500 });
    renderRoute();

    expect(
      await screen.findByText('The Availability Report could not be loaded.'),
    ).toBeInTheDocument();
  });
});

// Both placeholders are GONE. #77's Client Assignment Duration and #78's Assignment Start each
// replaced theirs with a real view, so `PendingReportPage.tsx` was deleted on the merge that brought
// the two together -- exactly what its own docstring said would happen. A test asserting "not built
// yet" would now assert the opposite of the truth, and the real views carry their own suites
// (`AssignmentDuration.test.tsx`, and #78's for the start lookup).
