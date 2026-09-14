import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { AssignmentDurationRoute } from '../../../../src/features/reports/assignment-duration/AssignmentDurationRoute';

/**
 * Four pairings. Deliberately NOT in longest-first order on the wire, so a screen that simply renders
 * what it received cannot pass the default-ordering test by accident.
 */
const ROWS = [
  {
    employeeId: 2,
    employeeName: 'Devon Brooks',
    clientId: 20,
    clientName: 'Scarlet Logistics',
    coachName: 'Priya Natarajan',
    totalDays: 1014,
    durationDisplay: '2 yrs 9 mos (1,014 days)',
    // Absent by default in this fixture: the name-only assertions below must keep matching a bare
    // name. `employeeType` is exercised on its own fixture further down.
    employeeType: null,
  },
  {
    employeeId: 1,
    employeeName: 'Priya Natarajan',
    clientId: 10,
    clientName: 'Olentangy Health',
    coachName: 'Jordan Wells',
    totalDays: 1238,
    durationDisplay: '3 yrs 4 mos (1,238 days)',
    employeeType: null,
  },
  {
    employeeId: 4,
    employeeName: 'Sam Okafor',
    clientId: 40,
    clientName: 'Foxfield Energy',
    // The no-coach case: present, never filtered (spec Edge Cases).
    coachName: null,
    totalDays: 443,
    durationDisplay: '1 yr 2 mos (443 days)',
    employeeType: null,
  },
  {
    employeeId: 3,
    employeeName: 'Maya Alvarez',
    clientId: 30,
    clientName: 'Buckeye Mutual',
    coachName: 'Alba Vance',
    totalDays: 842,
    durationDisplay: '2 yrs 4 mos (842 days)',
    employeeType: null,
  },
];

function ok(body: unknown) {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}

function status(code: number) {
  return { ok: false, status: code, json: async () => ({}) } as unknown as Response;
}

function renderReport() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AssignmentDurationRoute />
    </QueryClientProvider>,
  );
}

/** The rendered body rows, as arrays of cell text. */
function bodyRows(): string[][] {
  const table = screen.getByRole('table');
  return within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent ?? ''),
    );
}

/**
 * Clicks a column's sort control by its ACCESSIBLE name, not its visible label.
 *
 * `SortButton`'s `aria-label` is `Sort by <label lowercased>` — it extends the visible text rather than
 * replacing it, which is what keeps WCAG 2.5.3 satisfied for a voice-control user. Matching on the bare
 * label finds nothing.
 */
async function clickColumn(label: string) {
  await userEvent.click(screen.getByRole('button', { name: `Sort by ${label.toLowerCase()}` }));
}

describe('Client Assignment Duration report', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    fetchSpy.mockResolvedValue(ok(ROWS));
  });

  it('renders the mockup RPT-4 column set plus Employee Type — five columns, Coach among them (issue #386)', async () => {
    renderReport();

    // FR-014, AC-39, JM J20, research D-4, the read-surface contract and CHK019 all name Coach. No
    // build task in this phase did until 2026-08-19, so the column set is asserted here rather than
    // left to T102 in the final phase. Employee Type was added as its own column by issue #386.
    await waitFor(() =>
      expect(screen.getByRole('columnheader', { name: /EDJEr/ })).toBeInTheDocument(),
    );

    const headers = screen
      .getAllByRole('columnheader')
      .map((header) => header.textContent?.replace(/[▼▲\s]+$/, '').trim());
    expect(headers).toEqual(['EDJEr', 'Employee Type', 'Client', 'Coach', 'Duration']);
  });

  it('orders by total duration descending by default, whatever order the server sent', async () => {
    renderReport();

    await waitFor(() => expect(bodyRows()).toHaveLength(4));
    expect(bodyRows().map((cells) => cells[4])).toEqual([
      '3 yrs 4 mos (1,238 days)',
      '2 yrs 9 mos (1,014 days)',
      '2 yrs 4 mos (842 days)',
      '1 yr 2 mos (443 days)',
    ]);
  });

  it('marks the ordered column with aria-sort and leaves the others unmarked', async () => {
    renderReport();

    await waitFor(() => expect(bodyRows()).toHaveLength(4));

    const duration = screen.getByRole('columnheader', { name: /Duration/ });
    expect(duration).toHaveAttribute('aria-sort', 'descending');

    // "none" on the other three sortable columns, which is CORRECT and not a gap. Among a set of
    // sortable columns, aria-sort="none" means "sortable, not currently sorted"; omitting it entirely is
    // what `Table` reserves for a column that cannot sort at all — which Employee Type is (it renders a
    // plain string header, not a `TableSortableColumn`), so it is deliberately excluded from this loop.
    // The distinction is the primitive's, documented in its own remarks, and it is the one a
    // screen-reader user needs.
    for (const label of ['EDJEr', 'Client', 'Coach']) {
      // The HEADER's accessible name is its text ("EDJEr"); the nested button's aria-label
      // ("Sort by edjer") names the control, not the cell. Two different elements, two different names.
      expect(screen.getByRole('columnheader', { name: new RegExp(label) })).toHaveAttribute(
        'aria-sort',
        'none',
      );
    }
  });

  it('re-sorts on Duration when its header is chosen again, without re-fetching', async () => {
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(4));
    const callsBefore = fetchSpy.mock.calls.length;

    await clickColumn('Duration');

    // FR-014: "the ordering changes and the data set is unchanged" — so no second request.
    expect(bodyRows().map((cells) => cells[4])).toEqual([
      '1 yr 2 mos (443 days)',
      '2 yrs 4 mos (842 days)',
      '2 yrs 9 mos (1,014 days)',
      '3 yrs 4 mos (1,238 days)',
    ]);
    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  it('sorts on the DAY COUNT, not the display string', async () => {
    // The FR-030 trap: lexically "10 yrs" precedes "3 yrs". A screen ordering the rendered text would
    // put the ten-year pairing FIRST ascending, which looks entirely plausible.
    fetchSpy.mockResolvedValue(
      ok([
        { ...ROWS[0], totalDays: 3650, durationDisplay: '10 yrs (3,650 days)' },
        { ...ROWS[1], totalDays: 1095, durationDisplay: '3 yrs (1,095 days)' },
      ]),
    );
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(2));

    await clickColumn('Duration');

    expect(bodyRows().map((cells) => cells[4])).toEqual([
      '3 yrs (1,095 days)',
      '10 yrs (3,650 days)',
    ]);
  });

  it('sorts each of the other three columns too', async () => {
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(4));

    await clickColumn('EDJEr');
    expect(bodyRows().map((cells) => cells[0])).toEqual([
      'Devon Brooks',
      'Maya Alvarez',
      'Priya Natarajan',
      'Sam Okafor',
    ]);

    await clickColumn('Client');
    expect(bodyRows().map((cells) => cells[2])).toEqual([
      'Buckeye Mutual',
      'Foxfield Energy',
      'Olentangy Health',
      'Scarlet Logistics',
    ]);
  });

  it('renders a null coach as an empty cell and never drops the row', async () => {
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(4));

    const noCoach = bodyRows().find((cells) => cells[0] === 'Sam Okafor');
    expect(noCoach).toBeDefined();
    expect(noCoach?.[3]).toBe('');
  });

  it('sends a null coach to the END on A-Z and the START on Z-A', async () => {
    // Nulls rank AFTER every name and flip with the direction (owner decision 2026-08-20). The earlier
    // build pinned them last in BOTH directions, which made reversing the column a PARTIAL reversal --
    // and the rows that failed to move were the ones with an empty cell, so nothing on screen explained
    // why they stayed. Asserted in both directions, because either direction alone passes under both
    // rules and would not have caught this.
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(4));

    // The first click on Coach runs A-Z (INITIAL_DIRECTION.coach).
    await clickColumn('Coach');
    const azCoaches = bodyRows().map((cells) => cells[3]);
    expect(azCoaches.at(-1)).toBe('');
    const azNamed = azCoaches.filter((coach) => coach !== '');
    expect(azNamed).toEqual([...azNamed].sort((x, y) => x.localeCompare(y)));

    // The second click reverses it to Z-A, and the empty cell travels with the reversal.
    await clickColumn('Coach');
    const zaCoaches = bodyRows().map((cells) => cells[3]);
    expect(zaCoaches.at(0)).toBe('');

    // TOTAL reversal -- every row moved. This is the property pinning nulls broke, and it is the
    // assertion that fails under the old rule while the two positional checks above still pass.
    expect(zaCoaches).toEqual([...azCoaches].reverse());
  });

  it('moves a no-coach row that ARRIVED first to the end on A-Z', async () => {
    // The mirror of the case above, and it exists because the comparator sees its arguments in an order
    // the caller does not control: with the null row first in the payload, `Array.sort` asks
    // compare(named, null) rather than compare(null, named), which is the other branch of the null
    // ranking. One fixture ordering exercises one branch only.
    fetchSpy.mockResolvedValue(
      ok([
        { ...ROWS[2], employeeId: 9, employeeName: 'Zoe Nakamura', coachName: null },
        { ...ROWS[1], employeeId: 10, employeeName: 'Abe Lindqvist', coachName: 'Jordan Wells' },
      ]),
    );
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(2));

    await clickColumn('Coach');

    expect(bodyRows().map((cells) => cells[3])).toEqual(['Jordan Wells', '']);
  });

  it('keeps two no-coach rows stable relative to each other when sorting by Coach', async () => {
    // Both null is its own branch: neither row precedes the other on that column, so the comparison
    // must return 0 rather than arbitrarily picking one. Without it the two would swap on every click.
    fetchSpy.mockResolvedValue(
      ok([
        { ...ROWS[2], employeeId: 7, employeeName: 'Ada Vance', coachName: null },
        { ...ROWS[2], employeeId: 8, employeeName: 'Bo Chen', coachName: null },
      ]),
    );
    renderReport();
    await waitFor(() => expect(bodyRows()).toHaveLength(2));

    await clickColumn('Coach');
    const afterOne = bodyRows().map((cells) => cells[0]);
    await clickColumn('Coach');

    expect(bodyRows().map((cells) => cells[0])).toEqual(afterOne);
  });

  it('shows the employee type as its own column, following the EDJEr name (issue #386)', async () => {
    fetchSpy.mockResolvedValue(ok([{ ...ROWS[0], employeeType: 'Full Time' }]));
    renderReport();

    await waitFor(() => expect(bodyRows()).toHaveLength(1));

    // Five columns — Employee Type is its own column, second, rather than nested in the EDJEr cell.
    const headers = screen
      .getAllByRole('columnheader')
      .map((header) => header.textContent?.replace(/[▼▲\s]+$/, '').trim());
    expect(headers).toEqual(['EDJEr', 'Employee Type', 'Client', 'Coach', 'Duration']);

    const row = screen.getByText('Devon Brooks').closest('tr') as HTMLElement;
    const cells = within(row).getAllByRole('cell');
    expect(cells[1].textContent).toBe('Full Time');
  });

  it('renders an empty Employee Type cell when the EDJEr name is present but the type is absent', async () => {
    renderReport();

    await waitFor(() => expect(bodyRows()).toHaveLength(4));
    const row = bodyRows().find((cells) => cells[0] === 'Devon Brooks');
    expect(row).toBeDefined();
    expect(row?.[1]).toBe('');
  });

  it('says "1 pairing", singular, for a single row', async () => {
    fetchSpy.mockResolvedValue(ok([ROWS[0]]));
    renderReport();

    await waitFor(() => expect(screen.getByText('1 pairing')).toBeInTheDocument());
  });

  it('shows an explicit empty state rather than a header-only table', async () => {
    // Not theoretical: `Table`'s empty check once missed an empty array from `rows.map()` and rendered a
    // bare header row — the defect T054 recorded in the primitive.
    fetchSpy.mockResolvedValue(ok([]));
    renderReport();

    await waitFor(() =>
      expect(screen.getByText('No active client assignments.')).toBeInTheDocument(),
    );
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('counts the pairings beside the title, worded for the row grain', async () => {
    renderReport();

    // "pairings", not "EDJErs": the grain is the EDJEr-client pair, so an EDJEr working two clients
    // contributes two rows and a person-count would be wrong (FR-015).
    await waitFor(() => expect(screen.getByText('4 pairings')).toBeInTheDocument());
  });

  it('says you lack permission on a 403, rather than that the report is broken', async () => {
    // A Compass Admin reaching this by deep link is refused by the server (FR-019). The nav never
    // offered the link, so "could not be loaded" would describe a working system as broken.
    fetchSpy.mockResolvedValue(status(403));
    renderReport();

    await waitFor(() =>
      expect(
        screen.getByText(
          'You do not have permission to view the Client Assignment Duration report.',
        ),
      ).toBeInTheDocument(),
    );
    expect(
      screen.queryByText('The Client Assignment Duration report could not be loaded.'),
    ).not.toBeInTheDocument();
  });

  it('distinguishes a genuine failure from a refusal', async () => {
    fetchSpy.mockResolvedValue(status(500));
    renderReport();

    await waitFor(() =>
      expect(
        screen.getByText('The Client Assignment Duration report could not be loaded.'),
      ).toBeInTheDocument(),
    );
  });

  it('reads the report through apiFetch, never a bare fetch', async () => {
    renderReport();

    // The mock replaces api-url entirely, so a bare `fetch` in the hook would leave this spy uncalled.
    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith('/api/compass/reports/assignment-duration'),
    );
  });
});
