import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { SalesDashboardPage } from '../../../../src/features/sales-dashboard/SalesDashboardPage';
import type {
  DashboardBreakdownRow,
  DashboardCategory,
  DashboardLoad,
  SalesDashboardData,
} from '../../../../src/features/sales-dashboard/types';

const COUNTS: SalesDashboardData = {
  asOfDate: '2026-08-21',
  activeSowCount: 1,
  expiringSowCount: 0,
  confirmedRolloutCount: 0,
  beachCount: 0,
};

function loaded<T>(value: T): DashboardLoad<T> {
  return { kind: 'loaded', value };
}

function renderPage(
  rows: DashboardBreakdownRow[],
  selectedCategory: DashboardCategory = 'active-sows',
) {
  return render(
    <SalesDashboardPage
      dashboard={loaded(COUNTS)}
      isDashboardPending={false}
      isDashboardError={false}
      selectedCategory={selectedCategory}
      onSelectCategory={vi.fn()}
      breakdown={loaded(rows)}
      isBreakdownPending={false}
      isBreakdownError={false}
    />,
  );
}

/**
 * Clicks a column's sort control by its ACCESSIBLE name, matching the `SortButton` convention.
 *
 * Module-scoped rather than per-`describe`: three grids sort now (issues #464, #462, #463) and a
 * second copy of this would be a second thing to keep in step with `SortButton`'s naming.
 */
async function clickColumn(label: string) {
  await userEvent.click(screen.getByRole('button', { name: `Sort by ${label.toLowerCase()}` }));
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
 * The header labels, in render order, with the ordered column's caret glyph stripped.
 *
 * Read positionally rather than by accessible name on purpose: `columns` and `cells` are paired BY
 * INDEX inside `StackedRows`, so the index a label sits at is the fact worth asserting.
 */
function headerLabels(): string[] {
  return within(screen.getByRole('table'))
    .getAllByRole('columnheader')
    .map((header) => header.textContent?.replace(/[▼▲\s]+$/, '').trim() ?? '');
}

/** Each header's `aria-sort`, keyed by its label. */
function headerSortStates(): Record<string, string | null> {
  const headers = within(screen.getByRole('table')).getAllByRole('columnheader');
  return Object.fromEntries(
    headers.map((header, index) => [headerLabels()[index], header.getAttribute('aria-sort')]),
  );
}

describe('SalesDashboardPage — header (issue #343)', () => {
  it('does not render an "as of" date under the title — it is inherently as of the date you load the page', () => {
    renderPage([]);

    expect(screen.queryByText(/as of/i)).not.toBeInTheDocument();
  });
});

describe('SalesDashboardPage — active client assignments breakdown (issue #459, #633)', () => {
  /** Active-assignments headers are sort controls, so the ordered one carries a caret glyph — stripped here. */
  function headerLabels() {
    return within(screen.getByRole('table'))
      .getAllByRole('columnheader')
      .map((header) => header.textContent?.replace(/[▼▲\s]+$/, '').trim());
  }

  it('shows both the assignment start date and the (max) SOW end date, ahead of Coach', () => {
    renderPage([
      {
        employeeId: 1,
        employeeName: 'Dana Reed',
        employeeType: 'Full Time',
        clients: [{ id: 5, name: 'Nordhaven' }],
        startDate: '2026-03-01',
        date: '2026-12-31',
        daysUntil: null,
        daysAvailable: null,
        coachName: 'Priya Natarajan',
        coachId: 91,
      },
    ]);

    expect(headerLabels()).toEqual([
      'EDJEr',
      'Type',
      'Client',
      'Assignment Start',
      'SOW End Date',
      'Coach',
    ]);

    const row = within(screen.getByRole('table')).getAllByRole('row')[1];
    const cells = within(row)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells).toEqual([
      'Dana Reed',
      'Full Time',
      'Nordhaven',
      '03/01/2026',
      '12/31/2026',
      'Priya Natarajan',
    ]);
  });

  it('renders the assignment start date and the SOW end date for a single active assignment', () => {
    renderPage([
      {
        employeeId: 2,
        employeeName: 'Pat Nolan',
        employeeType: 'Part Time',
        clients: [{ id: 6, name: 'Quarry Ridge' }],
        startDate: '2026-01-15',
        date: '2026-11-30',
        daysUntil: null,
        daysAvailable: null,
        coachName: null,
        coachId: null,
      },
    ]);

    const table = screen.getByRole('table');
    const row = within(table).getAllByRole('row')[1];
    const cells = within(row)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells).toEqual([
      'Pat Nolan',
      'Part Time',
      'Quarry Ridge',
      '01/15/2026',
      '11/30/2026',
      '',
    ]);
  });
});

describe('SalesDashboardPage — employee type column (issue #332)', () => {
  it('renders the Type header and a Team-Directory-style pill for expiring SOWs', () => {
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="expiring-sows"
        onSelectCategory={vi.fn()}
        breakdown={loaded([
          {
            employeeId: 3,
            employeeName: 'Grant Ashby',
            employeeType: '1099',
            clients: [{ id: 7, name: 'Silverline' }],
            startDate: null,
            date: '2026-09-10',
            daysUntil: 20,
            daysAvailable: null,
            coachName: null,
            coachId: null,
          },
        ])}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );

    // Caret-stripped: these headers are sort controls since issue #462, so the ordered one carries a
    // trailing glyph — the same accommodation the beach grid's header assertion already makes.
    expect(headerLabels()).toEqual([
      'EDJEr',
      'Type',
      'Client',
      'SOW End Date',
      'Days Until Expiration',
      'Coach',
    ]);

    const table = screen.getByRole('table');
    // The mockups' `.pill.gray` — the exact styling/wording the Team Directory's own "Type"
    // column uses, per owner request on issue #332 (a StatusPill rather than plain text).
    const pill = within(table).getByText('1099');
    expect(pill.className).toContain('rounded-full');
  });

  it('renders the Type column for confirmed rollouts', () => {
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="confirmed-rollouts"
        onSelectCategory={vi.fn()}
        breakdown={loaded([
          {
            employeeId: 4,
            employeeName: 'Lorenzo Batista',
            employeeType: 'Full Time',
            clients: [{ id: 8, name: 'Brightpath' }],
            startDate: null,
            date: '2026-10-01',
            daysUntil: 40,
            daysAvailable: null,
            coachName: null,
            coachId: null,
          },
        ])}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );

    // Caret-stripped for the same reason as the expiring-SOWs case above (issue #463).
    expect(headerLabels()).toEqual([
      'EDJEr',
      'Type',
      'Client',
      'Assignment End Date',
      'Days Until Rollout',
      'Coach',
    ]);
    expect(within(screen.getByRole('table')).getByText('Full Time')).toBeInTheDocument();
  });

  it('does NOT render a Type column on the beach grid — a 1099 EDJEr is never on the beach', () => {
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="beach"
        onSelectCategory={vi.fn()}
        breakdown={loaded([
          {
            employeeId: 5,
            employeeName: 'Jamie Ortiz',
            employeeType: 'Full Time',
            clients: [{ id: 9, name: 'EDJE Internal' }],
            startDate: null,
            date: '2026-08-11',
            daysUntil: null,
            daysAvailable: 10,
            coachName: 'Priya Natarajan',
            coachId: 91,
          },
        ])}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );

    const table = screen.getByRole('table');
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((header) => header.textContent);
    expect(headers).not.toContain('Type');
  });
});

describe('SalesDashboardPage — beach breakdown (issue #331, issue #329)', () => {
  function renderBeachPage(rows: DashboardBreakdownRow[]) {
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="beach"
        onSelectCategory={vi.fn()}
        breakdown={loaded(rows)}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );
  }

  it('does not show a Client column, since every beach assignment is to an internal EDJE client, but does show Days Available', () => {
    renderBeachPage([
      {
        employeeId: 3,
        employeeName: 'Jordan Blake',
        employeeType: 'Full Time',
        clients: [{ id: 1, name: 'Leading EDJE (Internal)' }],
        startDate: null,
        date: '2026-07-01',
        daysUntil: null,
        daysAvailable: 10,
        coachName: 'Priya Natarajan',
        coachId: 91,
      },
    ]);

    const table = screen.getByRole('table');
    // The beach grid's headers are now sort controls (issue #464), so the ordered one carries a
    // trailing caret glyph — stripped here the same way the Client Assignment Duration report's own
    // header-set assertions do.
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((header) => header.textContent?.replace(/[▼▲\s]+$/, '').trim());
    expect(headers).toEqual(['EDJEr', 'Assignment Start Date', 'Days Available', 'Coach']);

    const row = within(table).getAllByRole('row')[1];
    const cells = within(row)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells).toEqual(['Jordan Blake', '07/01/2026', '10', 'Priya Natarajan']);
  });

  it('renders a blank Days Available cell when the value is null', () => {
    renderBeachPage([
      {
        employeeId: 2,
        employeeName: 'Robin Chen',
        employeeType: 'Full Time',
        clients: [{ id: 10, name: 'EDJE Internal' }],
        startDate: null,
        date: null,
        daysUntil: null,
        daysAvailable: null,
        coachName: null,
        coachId: null,
      },
    ]);

    const table = screen.getByRole('table');
    const row = within(table).getAllByRole('row')[1];
    const cells = within(row)
      .getAllByRole('cell')
      .map((cell) => cell.textContent);
    expect(cells).toEqual(['Robin Chen', '', '', '']);
  });
});

describe('SalesDashboardPage — expiring SOWs breakdown (issue #465)', () => {
  it('does not render the "only SOWs with no subsequent SOW/contract" footnote', () => {
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="expiring-sows"
        onSelectCategory={vi.fn()}
        breakdown={loaded([
          {
            employeeId: 4,
            employeeName: 'Sam Ortiz',
            employeeType: 'Full Time',
            clients: [{ id: 8, name: 'Brackenfield' }],
            startDate: null,
            date: '2026-09-15',
            daysUntil: 25,
            daysAvailable: null,
            coachName: null,
            coachId: null,
          },
        ])}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );

    expect(screen.queryByText(/only sows with no subsequent/i)).not.toBeInTheDocument();
  });
});

describe('SalesDashboardPage — beach breakdown sortable headers (issue #464)', () => {
  function renderBeachPage(rows: DashboardBreakdownRow[]) {
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="beach"
        onSelectCategory={vi.fn()}
        breakdown={loaded(rows)}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );
  }

  const ROWS: DashboardBreakdownRow[] = [
    {
      employeeId: 2,
      employeeName: 'Devon Brooks',
      employeeType: 'Full Time',
      clients: [{ id: 1, name: 'EDJE Internal' }],
      startDate: null,
      date: '2026-07-10',
      daysUntil: null,
      daysAvailable: 48,
      coachName: 'Priya Natarajan',
      coachId: 91,
    },
    {
      employeeId: 1,
      employeeName: 'Priya Natarajan',
      employeeType: 'Full Time',
      clients: [{ id: 1, name: 'EDJE Internal' }],
      startDate: null,
      date: '2026-06-01',
      daysUntil: null,
      daysAvailable: 88,
      coachName: 'Jordan Wells',
      coachId: 90,
    },
    {
      employeeId: 4,
      employeeName: 'Sam Okafor',
      employeeType: 'Full Time',
      clients: [{ id: 1, name: 'EDJE Internal' }],
      startDate: null,
      date: '2026-08-05',
      daysUntil: null,
      // No coach — the row is present regardless (FR-008).
      coachName: null,
      coachId: null,
      daysAvailable: 22,
    },
  ];

  it('orders by Assignment Start Date, oldest first, by default (owner decision on issue #464)', () => {
    renderBeachPage(ROWS);

    expect(bodyRows().map((cells) => cells[0])).toEqual([
      'Priya Natarajan',
      'Devon Brooks',
      'Sam Okafor',
    ]);
  });

  it('marks the ordered column with aria-sort and leaves the others "none"', () => {
    renderBeachPage(ROWS);

    const table = screen.getByRole('table');
    expect(
      within(table).getByRole('columnheader', { name: /Assignment Start Date/ }),
    ).toHaveAttribute('aria-sort', 'ascending');

    for (const label of ['EDJEr', 'Days Available', 'Coach']) {
      expect(within(table).getByRole('columnheader', { name: new RegExp(label) })).toHaveAttribute(
        'aria-sort',
        'none',
      );
    }
  });

  it('reverses the Assignment Start Date column when its ordered header is clicked', async () => {
    renderBeachPage(ROWS);

    await clickColumn('Assignment Start Date');

    expect(bodyRows().map((cells) => cells[0])).toEqual([
      'Sam Okafor',
      'Devon Brooks',
      'Priya Natarajan',
    ]);
  });

  it('sorts by EDJEr name A-Z on first click', async () => {
    renderBeachPage(ROWS);

    await clickColumn('EDJEr');

    expect(bodyRows().map((cells) => cells[0])).toEqual([
      'Devon Brooks',
      'Priya Natarajan',
      'Sam Okafor',
    ]);
  });

  it('sorts by Days Available as a NUMBER, not lexically', async () => {
    renderBeachPage(ROWS);

    await clickColumn('Days Available');

    // Descending on first click — most days on the beach first.
    expect(bodyRows().map((cells) => cells[2])).toEqual(['88', '48', '22']);
  });

  it('sends a null coach to the END on A-Z and the START on Z-A, matching the Client Assignment Duration report', async () => {
    renderBeachPage(ROWS);

    await clickColumn('Coach');
    const azCoaches = bodyRows().map((cells) => cells[3]);
    expect(azCoaches.at(-1)).toBe('');
    const azNamed = azCoaches.filter((coach) => coach !== '');
    expect(azNamed).toEqual([...azNamed].sort((x, y) => x.localeCompare(y)));

    await clickColumn('Coach');
    const zaCoaches = bodyRows().map((cells) => cells[3]);
    expect(zaCoaches.at(0)).toBe('');
    expect(zaCoaches).toEqual([...azCoaches].reverse());
  });

  // ---------------------------------------------------------------- the comparator's null paths
  //
  // The PR advertises one rule for every sortable column -- "null sorts to the END on ascending and
  // the START on descending, so a reversal is TOTAL" -- but only the Coach column's a-is-null branch
  // was exercised. Nine null paths exist across the four columns and one was covered, which is how a
  // rule stated in prose ends up holding for exactly one column.
  //
  // Two nulls in the SAME column is the case that matters most and was missing everywhere: it is the
  // only one that reaches the `return 0` stability branch, and a comparator that mis-handles it can
  // reorder equal rows on re-sort while every single-null test still passes.

  /** Two rows whose value in the column under test is null, plus one that has a value. */
  const TWO_NULLS: DashboardBreakdownRow[] = [
    {
      employeeId: 10,
      employeeName: 'Ana Reyes',
      employeeType: 'Full Time',
      clients: [{ id: 1, name: 'EDJE Internal' }],
      startDate: null,
      date: null,
      daysUntil: null,
      daysAvailable: null,
      coachName: null,
      coachId: null,
    },
    {
      employeeId: 11,
      employeeName: 'Bo Chen',
      employeeType: 'Full Time',
      clients: [{ id: 1, name: 'EDJE Internal' }],
      startDate: null,
      date: null,
      daysUntil: null,
      daysAvailable: null,
      coachName: null,
      coachId: null,
    },
    {
      employeeId: 12,
      employeeName: 'Cy Duval',
      employeeType: 'Full Time',
      clients: [{ id: 1, name: 'EDJE Internal' }],
      startDate: null,
      date: '2026-08-01',
      daysUntil: null,
      daysAvailable: 5,
      coachName: 'Wren Ellis',
      coachId: 92,
    },
  ];

  // The first click does NOT mean ascending everywhere: INITIAL_DIRECTION opens Days Available
  // DESCENDING, because the useful question there is who has waited longest. A test that assumed
  // "first click = ascending" would fail on that column for the right reason and be "fixed" by
  // loosening it, so each case states the direction its first click actually selects.
  it.each([
    ['Days Available', 2, 'start' as const],
    ['Coach', 3, 'end' as const],
  ])(
    'sends BOTH null %s rows together, and reverses them totally on the next click',
    async (label, cellIndex, nullsAfterFirstClick) => {
      renderBeachPage(TWO_NULLS);

      await clickColumn(label);
      const first = bodyRows().map((cells) => cells[cellIndex]);
      const firstNulls = nullsAfterFirstClick === 'start' ? first.slice(0, 2) : first.slice(-2);
      expect(firstNulls, `both null ${label} rows belong at the ${nullsAfterFirstClick}`).toEqual([
        '',
        '',
      ]);

      await clickColumn(label);
      const second = bodyRows().map((cells) => cells[cellIndex]);
      const secondNulls = nullsAfterFirstClick === 'start' ? second.slice(-2) : second.slice(0, 2);
      expect(secondNulls, 'and at the other end once reversed').toEqual(['', '']);
      expect(second, 'reversing must be TOTAL, not leave the nulls stranded').toEqual(
        [...first].reverse(),
      );
    },
  );

  it('sends both null Assignment Start Date rows to the end, then the start', async () => {
    // The default column, so it is already ascending on first render -- clicking once reverses it
    // rather than selecting it, which is itself the behaviour worth pinning.
    renderBeachPage(TWO_NULLS);

    const ascending = bodyRows().map((cells) => cells[1]);
    expect(ascending.slice(-2)).toEqual(['', '']);

    await clickColumn('Assignment Start Date');
    const descending = bodyRows().map((cells) => cells[1]);
    expect(descending.slice(0, 2)).toEqual(['', '']);
  });

  // Which side of the comparator sees the null depends on the INPUT order, not on the column: the
  // engine decides whether a given pair arrives as (null, value) or (value, null). Feeding both
  // orders is what reaches both guards; a single fixture exercises one and leaves the other dark,
  // which is exactly how these two lines survived the first pass of these tests.
  it.each([
    ['null first', true],
    ['value first', false],
  ])(
    'puts a null Days Available last on ascending, whichever order it arrives in (%s)',
    async (_label, nullFirst) => {
      const nullRow = TWO_NULLS[0];
      const valueRow = TWO_NULLS[2];
      renderBeachPage(nullFirst ? [nullRow, valueRow] : [valueRow, nullRow]);

      // Days Available opens descending, so one click puts it ascending.
      await clickColumn('Days Available');
      await clickColumn('Days Available');

      expect(bodyRows().map((cells) => cells[2])).toEqual(['5', '']);
    },
  );

  it.each([
    ['null first', true],
    ['value first', false],
  ])(
    'puts a null Assignment Start Date last on ascending, whichever order it arrives in (%s)',
    (_label, nullFirst) => {
      const nullRow = TWO_NULLS[0];
      const valueRow = TWO_NULLS[2];
      renderBeachPage(nullFirst ? [nullRow, valueRow] : [valueRow, nullRow]);

      // Assignment Start Date is the default column and opens ascending -- no click needed.
      expect(bodyRows().map((cells) => cells[1])).toEqual(['08/01/2026', '']);
    },
  );

  it('returns to ascending on a THIRD click, rather than sticking at descending', async () => {
    // The toggle's other half. Two clicks proved it can reverse; only a third proves it toggles
    // rather than latching, and a latched header is a control that silently stops responding.
    renderBeachPage(ROWS);

    await clickColumn('EDJEr');
    const first = bodyRows().map((cells) => cells[0]);

    await clickColumn('EDJEr');
    expect(bodyRows().map((cells) => cells[0])).toEqual([...first].reverse());

    await clickColumn('EDJEr');
    expect(bodyRows().map((cells) => cells[0])).toEqual(first);
  });

  it('renders the refusal alert instead of the grid when the breakdown is refused', () => {
    // A Compass Admin holding no reporting role reaches the page and is refused the breakdown by the
    // server (FR-019). The grid must not render an empty table as though there were simply no rows.
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="beach"
        onSelectCategory={vi.fn()}
        breakdown={{ kind: 'refused' }}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );

    expect(screen.getByText(/do not have permission to view this breakdown/i)).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('falls back to an empty grid when the breakdown settled with nothing', () => {
    // `sortedRows` is nullish whenever `breakdown` is, and the grid must render empty rather than
    // throw. Every flag is FALSE deliberately: the grid is behind
    // `!pending && !error && !refused`, so a test that set `isBreakdownPending` -- which this one
    // originally did -- renders the spinner instead and never reaches the fallback at all.
    render(
      <SalesDashboardPage
        dashboard={loaded(COUNTS)}
        isDashboardPending={false}
        isDashboardError={false}
        selectedCategory="beach"
        onSelectCategory={vi.fn()}
        breakdown={undefined}
        isBreakdownPending={false}
        isBreakdownError={false}
      />,
    );

    expect(screen.getByText(/no rows in this category/i)).toBeVisible();
  });
});

describe('SalesDashboardPage — active SOWs sortable headers (issue #459)', () => {
  // All SIX columns of this grid click-to-sort (issue #459). It is the only grid that renders both
  // ends of the SOW, so both dates are separately sortable — the SOW start date is the one field no
  // other sortable grid carries, which is why `startDate` exists on `BreakdownSortColumn`.
  const ROWS: DashboardBreakdownRow[] = [
    {
      employeeId: 1,
      employeeName: 'Bianca Cole',
      employeeType: 'Part Time',
      clients: [{ id: 1, name: 'Zephyr Ltd' }],
      startDate: '2026-05-01',
      date: '2026-12-01',
      daysUntil: null,
      daysAvailable: null,
      coachName: 'Yara Sol',
      coachId: 90,
    },
    {
      employeeId: 2,
      employeeName: 'Aaron Diaz',
      employeeType: 'Full Time',
      clients: [{ id: 2, name: 'Meridian' }],
      startDate: '2026-01-15',
      date: '2026-09-30',
      daysUntil: null,
      daysAvailable: null,
      // No coach — the row is present regardless (FR-008).
      coachName: null,
      coachId: null,
    },
    {
      employeeId: 3,
      employeeName: 'Cara Boyd',
      employeeType: '1099',
      clients: [{ id: 3, name: 'Apex Co' }],
      startDate: '2026-03-20',
      date: '2026-10-15',
      daysUntil: null,
      daysAvailable: null,
      coachName: 'Nadia Frost',
      coachId: 92,
    },
  ];

  it('opens on EDJEr name ascending — the server’s primary order (EmployeeName)', () => {
    // DEFAULT_SORT reproduces the repository order, whose primary key is the EDJEr name the cell
    // shows (`ActiveSowsBreakdown_IsOrderedByTheEdjerNameTheCellDisplays`).
    renderPage(ROWS);

    expect(bodyRows().map((cells) => cells[0])).toEqual(['Aaron Diaz', 'Bianca Cole', 'Cara Boyd']);
  });

  it('marks EDJEr ascending and the other five columns "none" on open', () => {
    renderPage(ROWS);

    expect(headerSortStates()).toMatchObject({
      EDJEr: 'ascending',
      Type: 'none',
      Client: 'none',
      'Assignment Start': 'none',
      'SOW End Date': 'none',
      Coach: 'none',
    });
  });

  it('reverses EDJEr on its first click, since it is already the ordered column', async () => {
    renderPage(ROWS);

    await clickColumn('EDJEr');
    expect(bodyRows().map((cells) => cells[0])).toEqual(['Cara Boyd', 'Bianca Cole', 'Aaron Diaz']);
  });

  it('sorts by Type A-Z on first click, reverses, then returns A-Z on a third click', async () => {
    renderPage(ROWS);

    await clickColumn('Type');
    const az = ['1099', 'Full Time', 'Part Time'];
    expect(bodyRows().map((cells) => cells[1])).toEqual(az);

    await clickColumn('Type');
    expect(bodyRows().map((cells) => cells[1])).toEqual([...az].reverse());

    // The toggle's other half: a third click must return to ascending, not latch at descending.
    await clickColumn('Type');
    expect(bodyRows().map((cells) => cells[1])).toEqual(az);
  });

  it('sorts by Client A-Z on first click, reading the single client name', async () => {
    renderPage(ROWS);

    await clickColumn('Client');
    expect(bodyRows().map((cells) => cells[2])).toEqual(['Apex Co', 'Meridian', 'Zephyr Ltd']);
  });

  it('sorts by Assignment Start ascending on first click', async () => {
    renderPage(ROWS);

    await clickColumn('Assignment Start');
    expect(bodyRows().map((cells) => cells[3])).toEqual(['01/15/2026', '03/20/2026', '05/01/2026']);
  });

  it('sorts by SOW End Date ascending on first click (it is no longer the default column)', async () => {
    renderPage(ROWS);

    await clickColumn('SOW End Date');
    expect(bodyRows().map((cells) => cells[4])).toEqual(['09/30/2026', '10/15/2026', '12/01/2026']);
  });

  it.each([
    ['Assignment Start', 3],
    ['SOW End Date', 4],
  ])(
    'orders %s CHRONOLOGICALLY across a year boundary — proof it sorts raw ISO, not the mm/dd/yyyy text',
    async (label, cellIndex) => {
      // Formatted, these are '12/01/2025' and '01/15/2026'. A lexical sort of that rendered text puts
      // January first ('0' < '1'); chronological order puts December 2025 first. Sorting the raw ISO
      // `yyyy-mm-dd` value is what makes the column chronological.
      renderPage([
        { ...ROWS[0], employeeName: 'Later', startDate: '2026-01-15', date: '2026-01-15' },
        { ...ROWS[1], employeeName: 'Earlier', startDate: '2025-12-01', date: '2025-12-01' },
      ]);

      await clickColumn(label);
      expect(bodyRows().map((cells) => cells[cellIndex])).toEqual(['12/01/2025', '01/15/2026']);
    },
  );

  it('sends a null coach to the END on A-Z and the START on Z-A (nullable coach)', async () => {
    renderPage(ROWS);

    await clickColumn('Coach');
    const az = bodyRows().map((cells) => cells[5]);
    expect(az.at(-1)).toBe('');
    const azNamed = az.filter((coach) => coach !== '');
    expect(azNamed).toEqual([...azNamed].sort((x, y) => x.localeCompare(y)));

    await clickColumn('Coach');
    const za = bodyRows().map((cells) => cells[5]);
    expect(za.at(0)).toBe('');
    expect(za).toEqual([...az].reverse());
  });

  // The comparator's null and empty guards. Active-SOWs rows always carry both dates and one client
  // in practice, but the guards exist so a reversal stays TOTAL rather than leaving null rows
  // stranded — the same rule every other grid's comparator holds. Two nulls in one column is the case
  // that reaches the `return 0` stability branch, and it is fed in both input orders so both
  // single-null guards are exercised (the engine decides which side of a pair a null arrives on).
  const TWO_NULLS: DashboardBreakdownRow[] = [
    {
      employeeId: 10,
      employeeName: 'Ana Reyes',
      employeeType: 'Full Time',
      clients: [],
      startDate: null,
      date: null,
      daysUntil: null,
      daysAvailable: null,
      coachName: null,
      coachId: null,
    },
    {
      employeeId: 11,
      employeeName: 'Bo Chen',
      employeeType: 'Full Time',
      clients: [],
      startDate: null,
      date: null,
      daysUntil: null,
      daysAvailable: null,
      coachName: null,
      coachId: null,
    },
    {
      employeeId: 12,
      employeeName: 'Cy Duval',
      employeeType: 'Full Time',
      clients: [{ id: 3, name: 'Apex Co' }],
      startDate: '2026-02-01',
      date: '2026-08-01',
      daysUntil: null,
      daysAvailable: null,
      coachName: 'Wren Ellis',
      coachId: 92,
    },
  ];

  it.each([
    ['Assignment Start', 3],
    ['SOW End Date', 4],
    ['Coach', 5],
  ])(
    'sends BOTH null %s rows together and reverses them totally (empty client cell too)',
    async (label, cellIndex) => {
      renderPage(TWO_NULLS);

      // Each column opens ascending — a first click orders ascending, nulls to the end — and the
      // second click reverses. Both are asserted as a TOTAL reversal.
      await clickColumn(label);
      const first = bodyRows().map((cells) => cells[cellIndex]);
      await clickColumn(label);
      const second = bodyRows().map((cells) => cells[cellIndex]);

      const firstNulls = first.filter((cell) => cell === '');
      expect(firstNulls, `both null ${label} rows are present`).toHaveLength(2);
      expect(second, 'reversing must be TOTAL, not leave the nulls stranded').toEqual(
        [...first].reverse(),
      );
    },
  );

  it.each([
    ['null first', true],
    ['value first', false],
  ])(
    'puts a null Assignment Start last on ascending, whichever order it arrives in (%s)',
    async (_label, nullFirst) => {
      const nullRow = TWO_NULLS[0];
      const valueRow = TWO_NULLS[2];
      renderPage(nullFirst ? [nullRow, valueRow] : [valueRow, nullRow]);

      await clickColumn('Assignment Start');
      expect(bodyRows().map((cells) => cells[3])).toEqual(['02/01/2026', '']);
    },
  );

  it('reads an empty client cell as blank when sorting by Client', async () => {
    renderPage(TWO_NULLS);

    await clickColumn('Client');
    // The two empty-client rows sort before the named one; the guard renders them blank, not "undefined".
    expect(bodyRows().map((cells) => cells[2])).toEqual(['', '', 'Apex Co']);
  });
});

const SORTABLE_BREAKDOWNS = [
  {
    category: 'expiring-sows' as const,
    issue: '#462',
    dateLabel: 'SOW End Date',
    daysLabel: 'Days Until Expiration',
  },
  {
    category: 'confirmed-rollouts' as const,
    issue: '#463',
    dateLabel: 'Assignment End Date',
    daysLabel: 'Days Until Rollout',
  },
];

describe.each(SORTABLE_BREAKDOWNS)(
  'SalesDashboardPage — $category sortable headers (issue $issue)',
  ({ category, dateLabel, daysLabel }) => {
    /**
     * FOUR rows whose ascending order differs under every column, AND differs from the order they
     * are passed in, AND differs from the reverse of the order the grid opens on:
     *
     * | column                   | ascending order                     |
     * |--------------------------|-------------------------------------|
     * | (as passed in)           | Casey, Ada, Dana, Bo                |
     * | EDJEr                    | Ada, Bo, Casey, Dana                |
     * | Type                     | Dana, Ada, Casey, Bo                |
     * | Client                   | Bo, Dana, Ada, Casey                |
     * | the date / the day count | Dana, Ada, Bo, Casey  (the default) |
     * | Coach                    | Casey, Dana, Bo, Ada  (null last)   |
     *
     * All six of those are distinct, which is the property the assertions below rest on: a grid that
     * sorted nothing, or that reversed the default column instead of switching to the clicked one,
     * matches NONE of the five expectations and fails every ordering case.
     *
     * **Three rows could not achieve that** — with three rows there are only six possible orders and
     * five columns to place, so one column inevitably lands on the input order or on the default's
     * reverse. The first version of this fixture did exactly that (Coach ascending WAS the input
     * order), and the numbers are worth recording because they were measured, by replacing
     * `sortedRows`' comparator with a bare `[...rows]` and leaving the headers alone:
     *
     * - the three-row fixture: **4 surviving tests** — `orders by Coach on a first click` and
     *   `sorts the date column and the day count independently`, on both grids. The second survived
     *   for a different reason, since fixed: it compared one render's order against a snapshot of
     *   its own earlier order, and two unsorted renders agree perfectly.
     * - this fixture, with those two cases asserting explicit orders: **0 surviving tests** across
     *   both grids, 35 failures overall.
     *
     * That mutation is also why there is no "but `aria-sort` moved, so a no-op is ruled out" caveat
     * here: `aria-sort` is set from state whether or not a single row moves, so it does not stand in
     * for an ordering assertion. Each case asserts both, independently.
     */
    const ROWS: DashboardBreakdownRow[] = [
      {
        employeeId: 21,
        employeeName: 'Casey Vaughn',
        employeeType: 'Intern',
        clients: [{ id: 41, name: 'Halloway Group' }],
        startDate: null,
        date: '2026-11-20',
        daysUntil: 80,
        daysAvailable: null,
        coachName: 'Ida Nakamura',
        coachId: 93,
      },
      {
        employeeId: 22,
        employeeName: 'Ada Whitfield',
        employeeType: 'Full Time',
        // Two clients: an EDJEr holding two assignments tied on the date that keys the category
        // (FR-011), which the cell renders comma-joined in list order.
        clients: [
          { id: 42, name: 'Corvus Bank' },
          { id: 43, name: 'Zenith Rail' },
        ],
        startDate: null,
        date: '2026-09-19',
        daysUntil: 18,
        daysAvailable: null,
        // No coach — the row is present regardless (FR-008), and its null is what the house null
        // rule is asserted against below.
        coachName: null,
        coachId: null,
      },
      {
        employeeId: 23,
        employeeName: 'Dana Ruiz',
        employeeType: '1099',
        clients: [{ id: 44, name: 'Brightpath' }],
        startDate: null,
        date: '2026-09-05',
        daysUntil: 4,
        daysAvailable: null,
        coachName: 'Priya Natarajan',
        coachId: 91,
      },
      {
        employeeId: 24,
        employeeName: 'Bo Ferraro',
        employeeType: 'Part Time',
        clients: [{ id: 45, name: 'Aldridge Foods' }],
        startDate: null,
        date: '2026-10-02',
        daysUntil: 31,
        daysAvailable: null,
        coachName: 'Wren Ellis',
        coachId: 92,
      },
    ];

    /** The order the grid opens on, and the order every ordering case starts from. */
    const DEFAULT_ORDER = ['Dana Ruiz', 'Ada Whitfield', 'Bo Ferraro', 'Casey Vaughn'];

    /** The EDJEr column, top to bottom — the identity of each row in order. */
    const names = () => bodyRows().map((cells) => cells[0]);

    function renderGrid(rows: DashboardBreakdownRow[]) {
      return renderPage(rows, category);
    }

    it('opens ordered by the day count, soonest first — the order the server sends', () => {
      renderGrid(ROWS);

      expect(names()).toEqual(DEFAULT_ORDER);
      expect(headerSortStates()[daysLabel]).toBe('ascending');
    });

    it('marks the ordered column with aria-sort and every other header "none"', () => {
      renderGrid(ROWS);

      expect(headerSortStates()).toEqual({
        EDJEr: 'none',
        Type: 'none',
        Client: 'none',
        [dateLabel]: 'none',
        [daysLabel]: 'ascending',
        Coach: 'none',
      });
    });

    // The four columns whose ordering is independent of the one the grid opens on, so a single click
    // both selects them and is the first ordering the viewer sees.
    it.each([
      ['EDJEr', ['Ada Whitfield', 'Bo Ferraro', 'Casey Vaughn', 'Dana Ruiz']],
      ['Type', ['Dana Ruiz', 'Ada Whitfield', 'Casey Vaughn', 'Bo Ferraro']],
      ['Client', ['Bo Ferraro', 'Dana Ruiz', 'Ada Whitfield', 'Casey Vaughn']],
      ['Coach', ['Casey Vaughn', 'Dana Ruiz', 'Bo Ferraro', 'Ada Whitfield']],
    ])('orders by %s on a first click, and moves aria-sort to it', async (label, expected) => {
      renderGrid(ROWS);

      await clickColumn(label);

      expect(names()).toEqual(expected);
      expect(headerSortStates()[label]).toBe('ascending');
      expect(headerSortStates()[daysLabel]).toBe('none');
    });

    it('reverses the ordered column when it is clicked, totally', async () => {
      // ONE click, because the day count is the column the grid ALREADY opens ordered by — choosing
      // the ordered column reverses it rather than re-selecting it (`INITIAL_DIRECTION`'s doc).
      renderGrid(ROWS);

      expect(names(), 'the grid must open on the default before this reverses it').toEqual(
        DEFAULT_ORDER,
      );

      await clickColumn(daysLabel);

      // The explicit order, not just `[...before].reverse()`: a comparison against a snapshot taken
      // from the same render is satisfied by any pair of orders that happen to be each other's
      // reverse, including two that are both wrong.
      expect(names()).toEqual(['Casey Vaughn', 'Bo Ferraro', 'Ada Whitfield', 'Dana Ruiz']);
      expect(names()).toEqual([...DEFAULT_ORDER].reverse());
      expect(headerSortStates()[daysLabel]).toBe('descending');
    });

    it(`orders by ${dateLabel} on the RAW ISO value, not on the mm/dd/yyyy text`, async () => {
      // Rendered as mm/dd/yyyy, so a lexical sort of the CELL would order by month across years and
      // look plausible while being wrong. The four base dates share a year, so the case that
      // actually separates the two is a fifth row in a different year whose month sorts early.
      renderGrid([
        ...ROWS,
        {
          employeeId: 25,
          employeeName: 'Dev Alonso',
          employeeType: 'Full Time',
          clients: [{ id: 46, name: 'Kestrel Logistics' }],
          startDate: null,
          // 01/14/2027 — FIRST by month text, LAST chronologically.
          date: '2027-01-14',
          daysUntil: 135,
          daysAvailable: null,
          coachName: 'Ida Nakamura',
          coachId: 93,
        },
      ]);

      await clickColumn(dateLabel);

      expect(names()).toEqual([...DEFAULT_ORDER, 'Dev Alonso']);
      expect(headerSortStates()[dateLabel]).toBe('ascending');
      expect(headerSortStates()[daysLabel]).toBe('none');
    });

    it('sorts the date column and the day count independently, though they agree', async () => {
      // The two are correlated by construction (the day count IS the date minus today), so ordering
      // by either produces the same rows in the same order. Both are still separately sortable —
      // both issues ask for "any of the headers" — and the fact worth pinning is that clicking the
      // date column MOVES the ordering to it rather than leaving the day count in charge, which is
      // the only externally visible difference between the two.
      renderGrid(ROWS);

      await clickColumn(dateLabel);

      // Both halves assert the EXPLICIT order. Comparing the second against a snapshot of the first
      // is what let this case survive replacing the sort with `[...rows]`: two unsorted renders agree
      // with each other perfectly.
      expect(names()).toEqual(DEFAULT_ORDER);
      expect(headerSortStates()[dateLabel]).toBe('ascending');
      expect(headerSortStates()[daysLabel]).toBe('none');

      await clickColumn(daysLabel);

      expect(names()).toEqual(DEFAULT_ORDER);
      expect(headerSortStates()[dateLabel]).toBe('none');
      expect(headerSortStates()[daysLabel]).toBe('ascending');
    });

    it('sends a null Coach to the END on A-Z and the START on Z-A', async () => {
      renderGrid(ROWS);

      await clickColumn('Coach');
      expect(bodyRows().map((cells) => cells[5])).toEqual([
        'Ida Nakamura',
        'Priya Natarajan',
        'Wren Ellis',
        '',
      ]);

      await clickColumn('Coach');
      // A TOTAL reversal — the null moves with the direction rather than staying stranded at the end.
      expect(bodyRows().map((cells) => cells[5])).toEqual([
        '',
        'Wren Ellis',
        'Priya Natarajan',
        'Ida Nakamura',
      ]);
    });

    it('sends a null day count to the END on ascending and the START on descending', async () => {
      // Not reachable from today's API — both categories are keyed on a non-null date, so the day
      // count is always computed. Asserted because the comparator's null branch exists and the house
      // rule has to hold for every column that CAN carry a null, not only the ones that do.
      //
      // The row WITH a count is passed last on purpose: ascending must lift it to the top, so the
      // first assertion is not satisfied by the input order.
      renderGrid([
        { ...ROWS[1], date: null, daysUntil: null },
        { ...ROWS[2], date: null, daysUntil: null },
        ROWS[0],
      ]);

      expect(bodyRows().map((cells) => cells[4])).toEqual(['80', '', '']);

      await clickColumn(daysLabel);
      expect(bodyRows().map((cells) => cells[4])).toEqual(['', '', '80']);
    });

    it('orders the Client column by the whole comma-joined cell, not just the first client', async () => {
      // Two rows tied on their FIRST client. The cell shows both names, so the key the grid orders on
      // is the text the viewer can compare by eye — a first-client-only key would leave these two in
      // whatever order they arrived in and give a viewer no way to predict it from the screen.
      renderGrid([
        {
          ...ROWS[1],
          employeeId: 51,
          employeeName: 'Rowan Diaz',
          clients: [
            { id: 42, name: 'Corvus Bank' },
            { id: 43, name: 'Zenith Rail' },
          ],
        },
        {
          ...ROWS[1],
          employeeId: 52,
          employeeName: 'Sasha Kim',
          clients: [
            { id: 42, name: 'Corvus Bank' },
            { id: 46, name: 'Halloway Group' },
          ],
        },
      ]);

      await clickColumn('Client');

      expect(names()).toEqual(['Sasha Kim', 'Rowan Diaz']);
    });

    it('pairs every header with the cell under it, in order', () => {
      // `StackedRows` pairs `columns` and `cells` BY INDEX, and this page builds both from per-
      // category spread conditionals of differing widths. A construction that diverged by one would
      // label every value with its neighbour's header and fail NOTHING — so the pairing is asserted
      // as a map, not as two lists of the right length.
      renderGrid([ROWS[0]]);

      const labels = headerLabels();
      const cells = bodyRows()[0];
      expect(labels).toHaveLength(6);
      expect(cells).toHaveLength(labels.length);
      expect(Object.fromEntries(labels.map((label, index) => [label, cells[index]]))).toEqual({
        EDJEr: 'Casey Vaughn',
        Type: 'Intern',
        Client: 'Halloway Group',
        [dateLabel]: '11/20/2026',
        [daysLabel]: '80',
        Coach: 'Ida Nakamura',
      });
    });
  },
);

describe('SalesDashboardPage — sorting is per grid, not shared across tiles', () => {
  const ROW = (overrides: Partial<DashboardBreakdownRow>): DashboardBreakdownRow => ({
    employeeId: 60,
    employeeName: 'Nils Aaberg',
    employeeType: 'Full Time',
    clients: [{ id: 70, name: 'Ashfield Mutual' }],
    startDate: '2026-01-01',
    date: '2026-09-01',
    daysUntil: 10,
    daysAvailable: 5,
    coachName: 'Wren Ellis',
    coachId: 92,
    ...overrides,
  });

  // Ordered ['Ola Bergman', 'Nils Aaberg'] by either default (the day count on expiring SOWs, the
  // start date on beach) and ['Nils Aaberg', 'Ola Bergman'] by EDJEr — so which of the two is on
  // screen says which sort is in force.
  const ROWS = [
    ROW({ employeeId: 60, employeeName: 'Nils Aaberg', daysUntil: 60, date: '2026-11-01' }),
    ROW({ employeeId: 61, employeeName: 'Ola Bergman', daysUntil: 10, date: '2026-09-15' }),
  ];

  const BY_DEFAULT = ['Ola Bergman', 'Nils Aaberg'];
  const BY_EDJER = ['Nils Aaberg', 'Ola Bergman'];

  /** The page on one tile — `selectedCategory` is a PROP, so a tile change is a re-render. */
  const onTile = (category: DashboardCategory) => (
    <SalesDashboardPage
      dashboard={loaded(COUNTS)}
      isDashboardPending={false}
      isDashboardError={false}
      selectedCategory={category}
      onSelectCategory={vi.fn()}
      breakdown={loaded(ROWS)}
      isBreakdownPending={false}
      isBreakdownError={false}
    />
  );

  const names = () => bodyRows().map((cells) => cells[0]);

  it('returns to the new grid’s own default order when the selected tile changes', async () => {
    // The sort state cannot simply follow the viewer from tile to tile: the four grids do not share a
    // column set, so a column chosen on one can be absent from the next — which would order the new
    // grid by a key none of its headers names, with no `aria-sort` anywhere to say so.
    const { rerender } = render(onTile('expiring-sows'));

    await clickColumn('EDJEr');
    expect(names()).toEqual(BY_EDJER);

    rerender(onTile('beach'));

    // Beach opens on its own default (assignment start date, oldest first), not on the EDJEr column
    // chosen while a different tile was selected.
    expect(names()).toEqual(BY_DEFAULT);
    expect(headerSortStates()['Assignment Start Date']).toBe('ascending');
    expect(headerSortStates()['EDJEr']).toBe('none');
  });

  it('RESTORES the sort when the same tile is returned to, having sorted nothing in between', async () => {
    // The other half of one-slot state, and the reason the comment on `chosenSort` says "reads as not
    // chosen" rather than "is discarded": leaving a tile does not clear the slot, so coming back to it
    // finds the chosen sort still there and re-applies it.
    const { rerender } = render(onTile('expiring-sows'));

    await clickColumn('EDJEr');
    expect(names()).toEqual(BY_EDJER);

    rerender(onTile('beach'));
    expect(names(), 'the beach grid must show its own default while it is selected').toEqual(
      BY_DEFAULT,
    );

    rerender(onTile('expiring-sows'));
    expect(names()).toEqual(BY_EDJER);
    expect(headerSortStates()['EDJEr']).toBe('ascending');
  });

  it('does NOT restore it when another tile was sorted in between — one slot, last one wins', async () => {
    // The consequence of a single slot rather than a `Record` per category, asserted rather than left
    // for someone to discover: sorting a second grid overwrites the first grid's choice, so returning
    // to it shows its default. Nothing has asked for per-category retention; a viewer who changes
    // tiles has changed subject.
    const { rerender } = render(onTile('expiring-sows'));

    await clickColumn('EDJEr');
    expect(names()).toEqual(BY_EDJER);

    rerender(onTile('confirmed-rollouts'));
    await clickColumn('EDJEr');
    expect(headerSortStates()['EDJEr']).toBe('ascending');

    rerender(onTile('expiring-sows'));
    expect(names()).toEqual(BY_DEFAULT);
    expect(headerSortStates()['EDJEr']).toBe('none');
    expect(headerSortStates()['Days Until Expiration']).toBe('ascending');
  });
});

describe('SalesDashboardPage — every grid pairs its headers with its cells', () => {
  // The four category shapes are 6, 6, 6 and 4 columns wide and each is built by spreading the same
  // per-category predicates into three separate arrays (`columns`, `cellClassNames`, `cells`). This
  // is the cheap total guard: every category, header count against cell count, with the count itself
  // asserted so a category that rendered no grid at all could not pass.
  it.each([
    ['active-sows' as const, 6],
    ['expiring-sows' as const, 6],
    ['confirmed-rollouts' as const, 6],
    ['beach' as const, 4],
  ])('%s has %i headers and exactly that many cells', (category, expectedWidth) => {
    renderPage(
      [
        {
          employeeId: 80,
          employeeName: 'Iris Vanto',
          employeeType: 'Full Time',
          clients: [{ id: 90, name: 'Kestrel Logistics' }],
          startDate: '2026-03-15',
          date: '2026-10-31',
          daysUntil: 60,
          daysAvailable: 12,
          coachName: 'Wren Ellis',
          coachId: 92,
        },
      ],
      category,
    );

    expect(headerLabels()).toHaveLength(expectedWidth);
    expect(bodyRows()[0]).toHaveLength(expectedWidth);
  });
});

describe('SalesDashboardPage — EDJEr and coach detail links (issue #330)', () => {
  /**
   * The row every case below starts from: an EDJEr with a coach, on the active-sows grid.
   * Overridden per test rather than re-declared, so a case says only what it is about.
   */
  const COACHED_ROW: DashboardBreakdownRow = {
    employeeId: 7,
    employeeName: 'Dana Reed',
    employeeType: 'Full Time',
    clients: [{ id: 5, name: 'Nordhaven' }],
    startDate: '2026-03-01',
    date: '2026-12-31',
    daysUntil: 132,
    daysAvailable: null,
    coachName: 'Priya Natarajan',
    coachId: 91,
  };

  it("renders the EDJEr's name as a link to their record, not plain text", () => {
    renderPage([COACHED_ROW]);

    expect(screen.getByRole('link', { name: 'Dana Reed' })).toHaveAttribute(
      'href',
      '/compass/team-directory/7',
    );
  });

  it("renders the coach's name as a link to THEIR record — the coach's id, not the EDJEr's", () => {
    renderPage([COACHED_ROW]);

    expect(screen.getByRole('link', { name: 'Priya Natarajan' })).toHaveAttribute(
      'href',
      '/compass/team-directory/91',
    );
  });

  it('leaves the coach cell empty and link-free when the EDJEr has no coach (FR-008)', () => {
    renderPage([{ ...COACHED_ROW, coachName: null, coachId: null }]);

    const cells = within(within(screen.getByRole('table')).getAllByRole('row')[1]).getAllByRole(
      'cell',
    );
    // Last column is Coach on every category shape.
    const coachCell = cells[cells.length - 1];
    expect(coachCell).toHaveTextContent('');
    expect(within(coachCell).queryByRole('link')).not.toBeInTheDocument();
    // The EDJEr's own link is unaffected — a coachless row still drills in.
    expect(screen.getByRole('link', { name: 'Dana Reed' })).toBeInTheDocument();
  });

  it('renders a coach NAME with no id as plain text rather than a link to nowhere', () => {
    // Not reachable from today's API (the DTO nulls both together) — asserted because the guard is
    // what keeps it unreachable, and a half-populated pair would otherwise render `href=".../null"`.
    renderPage([{ ...COACHED_ROW, coachId: null }]);

    expect(screen.queryByRole('link', { name: 'Priya Natarajan' })).not.toBeInTheDocument();
    expect(screen.getByText('Priya Natarajan')).toBeInTheDocument();
  });

  it('links both the EDJEr and the coach on the beach grid too, whose column set differs', () => {
    renderPage(
      [
        {
          ...COACHED_ROW,
          employeeId: 12,
          employeeName: 'Sam Okafor',
          clients: [],
          date: '2026-07-01',
          daysUntil: null,
          daysAvailable: 51,
          startDate: null,
        },
      ],
      'beach',
    );

    expect(screen.getByRole('link', { name: 'Sam Okafor' })).toHaveAttribute(
      'href',
      '/compass/team-directory/12',
    );
    expect(screen.getByRole('link', { name: 'Priya Natarajan' })).toHaveAttribute(
      'href',
      '/compass/team-directory/91',
    );
  });
});
