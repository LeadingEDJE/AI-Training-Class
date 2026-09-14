import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import {
  AssignmentStartPage,
  INVERTED_RANGE_MESSAGE,
  MISSING_DATE_MESSAGE,
} from '../../../../src/features/reports/assignment-start/AssignmentStartPage';
import type { AssignmentStartRow, ReportLoad } from '../../../../src/features/reports/types';

const ROWS: AssignmentStartRow[] = [
  {
    employeeName: 'Aisha Thompson',
    employeeType: 'Full Time',
    clientName: 'Buckeye Mutual',
    startDate: '2026-01-05',
  },
  // No employee type -- must still render, with an empty Employee Type cell.
  {
    employeeName: 'Omar Haddad',
    employeeType: null,
    clientName: 'EDJE Internal',
    startDate: '2026-06-15',
  },
];

function loaded(rows: AssignmentStartRow[]): ReportLoad<AssignmentStartRow[]> {
  return { kind: 'loaded', value: rows };
}

function renderPage(overrides: Partial<Parameters<typeof AssignmentStartPage>[0]> = {}) {
  const onRun = vi.fn();
  render(
    <AssignmentStartPage
      report={undefined}
      isPending={false}
      isError={false}
      onRun={onRun}
      {...overrides}
    />,
  );
  return { onRun };
}

/**
 * The Start Date FIELD.
 *
 * `{ selector: 'input' }` is load-bearing: `getByLabelText` also matches an `aria-label`, and since
 * issue #461 the Assignment Start Date header carries "Sort by assignment start date" — so on a screen
 * showing results, an unscoped `/start date/i` finds the input and that button.
 */
function startDateField() {
  return screen.getByLabelText(/start date/i, { selector: 'input' });
}

async function runLookup(from: string, to: string) {
  if (from !== '') {
    await userEvent.type(startDateField(), from);
  }
  if (to !== '') {
    await userEvent.type(screen.getByLabelText(/end date/i), to);
  }
  await userEvent.click(screen.getByRole('button', { name: 'Run' }));
}

describe('AssignmentStartPage (US4, AC-40, #78)', () => {
  describe('report heading (RPT-5 parity with the sibling reports)', () => {
    it('renders an "Assignment Start" heading like Availability and Duration do', () => {
      // The mockup gives RPT-5 an <h2>Assignment Start</h2> inside its card, and the two sibling
      // reports render exactly that via <Card title=…>. This screen shipped without it — the name
      // appeared only on the active tab — so it was the odd one out among the three. Level 2 matches
      // Card's heading level (see ReportsLayout.test.tsx, which asserts the siblings the same way).
      renderPage();

      expect(
        screen.getByRole('heading', { level: 2, name: 'Assignment Start' }),
      ).toBeInTheDocument();
    });
  });

  describe('range validation — the query must not be issued for input the page rejects', () => {
    it('refuses an inverted range with a message and issues NO query', async () => {
      // Spec US4 scenario 2 is explicit that no query is issued, not merely that its result is
      // discarded. Asserting `onRun` was never called is what holds that: a version that fetched and
      // then threw the answer away would satisfy a message-only assertion.
      const { onRun } = renderPage();

      await runLookup('2026-03-31', '2026-03-01');

      expect(screen.getByText(INVERTED_RANGE_MESSAGE)).toBeInTheDocument();
      expect(onRun).not.toHaveBeenCalled();
    });

    it('accepts an equal start and end as a single-day range', async () => {
      // The guard is `from > to`, never `>=`. A one-day lookup is a legitimate question and is the
      // likeliest off-by-one in this validation.
      const { onRun } = renderPage();

      await runLookup('2026-03-15', '2026-03-15');

      expect(screen.queryByText(INVERTED_RANGE_MESSAGE)).not.toBeInTheDocument();
      expect(onRun).toHaveBeenCalledWith({ from: '2026-03-15', to: '2026-03-15' });
    });

    it('refuses a missing date and issues no query', async () => {
      const { onRun } = renderPage();

      await runLookup('2026-03-01', '');

      expect(screen.getByText(MISSING_DATE_MESSAGE)).toBeInTheDocument();
      expect(onRun).not.toHaveBeenCalled();
    });

    it('refuses BOTH dates missing, not just one', async () => {
      // The guard is `from === '' || to === ''`. The sibling case above fills Start and leaves End
      // empty, so it only ever reaches the SECOND operand; both-empty short-circuits on the first.
      // An empty Run — the state the screen actually loads in, and the most likely thing a user
      // clicks first — was asserted nowhere in this file.
      //
      // It WAS asserted, in a browser: `reports.visual.spec.ts` clicked Run on a pristine form to
      // capture `assignment-start-error.png`. Issue #574 deleted that spec with the rest of the
      // visual gate, and this is the replacement at the layer TEST-STRATEGY ruling 3 puts it —
      // a validation-message render belongs in Vitest, not in a browser.
      const { onRun } = renderPage();

      await runLookup('', '');

      expect(screen.getByText(MISSING_DATE_MESSAGE)).toBeInTheDocument();
      expect(onRun).not.toHaveBeenCalled();
    });

    it('runs a valid range and passes it through unchanged', async () => {
      const { onRun } = renderPage();

      await runLookup('2026-03-01', '2026-03-31');

      expect(onRun).toHaveBeenCalledWith({ from: '2026-03-01', to: '2026-03-31' });
    });

    it('clears a previous message once a valid range is submitted', async () => {
      const { onRun } = renderPage();

      await runLookup('2026-03-31', '2026-03-01');
      expect(screen.getByText(INVERTED_RANGE_MESSAGE)).toBeInTheDocument();

      // Correct the start date to precede the end date.
      await userEvent.clear(startDateField());
      await userEvent.type(startDateField(), '2026-02-01');
      await userEvent.click(screen.getByRole('button', { name: 'Run' }));

      expect(screen.queryByText(INVERTED_RANGE_MESSAGE)).not.toBeInTheDocument();
      expect(onRun).toHaveBeenCalledWith({ from: '2026-02-01', to: '2026-03-01' });
    });

    it('associates the message with the Start Date field rather than floating it', async () => {
      renderPage();

      await runLookup('2026-03-31', '2026-03-01');

      // `FormField` wires its error through aria-describedby. A message a screen reader cannot tie to
      // a field is not a field validation message, so the association is asserted rather than the mere
      // presence of the text somewhere on the page.
      const field = startDateField();
      const describedBy = field.getAttribute('aria-describedby') ?? '';
      expect(describedBy, 'the field must describe itself with the error').not.toBe('');

      const described = describedBy
        .split(' ')
        .map((id) => document.getElementById(id)?.textContent ?? '')
        .join(' ');
      expect(described).toContain(INVERTED_RANGE_MESSAGE);
    });
  });

  describe('results', () => {
    it('renders a row per assignment with the mockup columns', () => {
      renderPage({ report: loaded(ROWS) });

      expect(screen.getByRole('columnheader', { name: 'EDJEr Name' })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Employee Type' })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Client Name' })).toBeInTheDocument();
      expect(
        screen.getByRole('columnheader', { name: 'Assignment Start Date' }),
      ).toBeInTheDocument();
      expect(screen.getByText('Aisha Thompson')).toBeInTheDocument();
      expect(screen.getByText('Omar Haddad')).toBeInTheDocument();
    });

    it('renders the employee type as its own column, following the name (issue #386)', () => {
      renderPage({ report: loaded(ROWS) });

      const row = screen.getByText('Aisha Thompson').closest('tr') as HTMLElement;
      const cells = within(row).getAllByRole('cell');

      // EDJEr Name, Employee Type, Client Name, Assignment Start Date.
      expect(cells[1].textContent).toBe('Full Time');
    });

    it('renders an empty Employee Type cell when the EDJEr has none', () => {
      renderPage({ report: loaded(ROWS) });

      const row = screen.getByText('Omar Haddad').closest('tr') as HTMLElement;
      const cells = within(row).getAllByRole('cell');

      expect(cells[1].textContent).toBe('');
    });

    it('renders start dates as mm/dd/yyyy, never the raw ISO string', () => {
      // Issue #234. The ISO form is asserted ABSENT as well as the formatted one present -- without
      // that half, a row rendering both would pass.
      renderPage({ report: loaded(ROWS) });

      expect(screen.getByText('01/05/2026')).toBeInTheDocument();
      expect(screen.queryByText('2026-01-05')).not.toBeInTheDocument();
    });

    it('shows an explicit empty state rather than a blank table', () => {
      // Spec US4 scenario 3.
      renderPage({ report: loaded([]) });

      expect(screen.getByText('No assignments started in that date range.')).toBeInTheDocument();
    });

    it('shows nothing at all before a lookup has been run', () => {
      // "You have not asked yet" and "your range matched nothing" are different things to say.
      // Rendering the empty state on arrival would tell the user their range found nothing before
      // they picked one.
      renderPage();

      expect(
        screen.queryByText('No assignments started in that date range.'),
      ).not.toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });

    it('reports a refusal as a refusal, not as a failure', () => {
      renderPage({ report: { kind: 'refused' } });

      expect(screen.getByText('You do not have access to this report.')).toBeInTheDocument();
    });

    it('reports a failed load', () => {
      renderPage({ report: { kind: 'failed' }, isError: true });

      expect(screen.getByText('The lookup could not be run. Try again.')).toBeInTheDocument();
    });

    it('reports an error even when no body arrived', () => {
      renderPage({ report: { kind: 'failed' } });

      expect(screen.getByText('The lookup could not be run. Try again.')).toBeInTheDocument();
    });

    it('renders nothing while the first lookup is in flight', () => {
      renderPage({ isPending: true });

      expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });
  });
});

describe('AssignmentStartPage — sortable headers (issue #461)', () => {
  /**
   * Three rows whose three orderings are all DIFFERENT from one another, so no assertion below can
   * pass by accident against the ordering some other column would have produced:
   *
   * | order                 | rows                |
   * |-----------------------|---------------------|
   * | start date ascending  | Zoe, Omar, Aisha    |
   * | EDJEr name ascending  | Aisha, Omar, Zoe    |
   * | client name ascending | Omar, Aisha, Zoe    |
   *
   * The start dates additionally straddle a year boundary, which is what makes the default-order
   * assertion able to see the mm/dd/yyyy trap: sorting the DISPLAYED strings gives
   * `01/05/2026` < `06/15/2026` < `12/31/2025` — Omar, Aisha, Zoe — a plausible-looking order that
   * puts the oldest assignment last.
   */
  const SORT_ROWS: AssignmentStartRow[] = [
    {
      employeeName: 'Zoe Alvarez',
      employeeType: null,
      clientName: 'Zenith Freight',
      startDate: '2025-12-31',
    },
    {
      employeeName: 'Aisha Thompson',
      employeeType: null,
      clientName: 'Marlowe Group',
      startDate: '2026-06-15',
    },
    {
      employeeName: 'Omar Haddad',
      employeeType: null,
      clientName: 'Ardent Mills',
      startDate: '2026-01-05',
    },
  ];

  /** Clicks a column's sort control by its ACCESSIBLE name, matching the `SortButton` convention. */
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

  /** The EDJEr-name column, which identifies a row uniquely in every fixture here. */
  function names(): string[] {
    return bodyRows().map((cells) => cells[0]);
  }

  it('orders by start date ascending by default, chronologically and not by the displayed string', () => {
    // The server already returns these rows ordered by StartDate ascending
    // (`CompassReportRepository.GetAssignmentStartsAsync`), so this default is the order the screen
    // opened on before sorting existed — nothing a reader or an existing expectation relied on moves.
    renderPage({ report: loaded(SORT_ROWS) });

    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);
  });

  it('marks the ordered column with aria-sort and leaves the other two "none"', () => {
    renderPage({ report: loaded(SORT_ROWS) });

    const table = screen.getByRole('table');
    expect(
      within(table).getByRole('columnheader', { name: /Assignment Start Date/ }),
    ).toHaveAttribute('aria-sort', 'ascending');

    for (const label of ['EDJEr Name', 'Client Name']) {
      expect(within(table).getByRole('columnheader', { name: new RegExp(label) })).toHaveAttribute(
        'aria-sort',
        'none',
      );
    }
  });

  it('sorts by EDJEr Name A→Z on the first click, and moves aria-sort with it', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('EDJEr Name');

    expect(names()).toEqual(['Aisha Thompson', 'Omar Haddad', 'Zoe Alvarez']);

    const table = screen.getByRole('table');
    expect(within(table).getByRole('columnheader', { name: /EDJEr Name/ })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    expect(
      within(table).getByRole('columnheader', { name: /Assignment Start Date/ }),
    ).toHaveAttribute('aria-sort', 'none');
  });

  it('sorts by Client Name A→Z on the first click', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');

    expect(names()).toEqual(['Omar Haddad', 'Aisha Thompson', 'Zoe Alvarez']);
  });

  // The FIRST click on this column reverses it, because it is the column that already orders the
  // table — `INITIAL_DIRECTION` decides a direction only when the choice moves off another column.
  it('reverses the start date column on the first click and restores it on the second', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Assignment Start Date');
    expect(names()).toEqual(['Aisha Thompson', 'Omar Haddad', 'Zoe Alvarez']);
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', {
        name: /Assignment Start Date/,
      }),
    ).toHaveAttribute('aria-sort', 'descending');

    await clickColumn('Assignment Start Date');
    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);
  });

  it('reverses a name column on a second click', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');
    await clickColumn('Client Name');

    expect(names()).toEqual(['Zoe Alvarez', 'Aisha Thompson', 'Omar Haddad']);
  });

  it('starts a newly chosen column at its own direction rather than inheriting the previous one', async () => {
    // Reverse one column, then choose another. The new column must open A→Z, not descending — a
    // single shared direction variable would carry the reversal across and is the likeliest defect.
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('EDJEr Name');
    await clickColumn('EDJEr Name');
    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);

    await clickColumn('Client Name');

    expect(names()).toEqual(['Omar Haddad', 'Aisha Thompson', 'Zoe Alvarez']);
  });

  it('keeps the server order for rows that share a start date', async () => {
    // The server orders StartDate then EmployeeName, and `Array.prototype.sort` is stable, so equal
    // dates keep the order they arrived in. The fixture is deliberately NOT alphabetical: a
    // comparator that re-tiebroke by name would reorder it, and one that did nothing would not.
    const tied: AssignmentStartRow[] = [
      { employeeName: 'Bea Nolan', clientName: 'Ardent Mills', startDate: '2026-02-01' },
      { employeeName: 'Al Cruz', clientName: 'Zenith Freight', startDate: '2026-02-01' },
    ];
    renderPage({ report: loaded(tied) });

    expect(names()).toEqual(['Bea Nolan', 'Al Cruz']);
  });

  it('sorts without re-issuing the lookup', async () => {
    // FR-014's "the ordering changes and the data set is unchanged" for the sibling duration report
    // applies here too: the rows are already materialised and re-ordering them is a presentation
    // concern. Asserting `onRun` is what holds it — a version that re-fetched and re-rendered the
    // same rows in the new order would satisfy an order-only assertion.
    const { onRun } = renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');
    await clickColumn('Assignment Start Date');

    expect(onRun).not.toHaveBeenCalled();
  });

  it('leaves the export control alone when the order changes', async () => {
    // The export URL is built from the range that was RUN, so re-sorting must not disturb it. If a
    // click reset that state the control would disappear, which is the visible half of the bug.
    renderPage({ report: loaded(SORT_ROWS) });
    await runLookup('2026-01-01', '2026-12-31');
    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeInTheDocument();

    await clickColumn('Client Name');

    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeInTheDocument();
  });

  it('returns to the default order when a new lookup is run', async () => {
    // Deliberate: a lookup is a distinct question, and its answer opens in the server's own order.
    // See the comment on `DEFAULT_SORT` for why this is reset explicitly rather than left to
    // whether the new range happens to be cached.
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');
    expect(names()).toEqual(['Omar Haddad', 'Aisha Thompson', 'Zoe Alvarez']);

    await runLookup('2026-01-01', '2026-12-31');

    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', {
        name: /Assignment Start Date/,
      }),
    ).toHaveAttribute('aria-sort', 'ascending');
  });
});
