import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import {
  SowExtensionPage,
  INVERTED_RANGE_MESSAGE,
  MISSING_DATE_MESSAGE,
} from '../../../../src/features/reports/sow-extension/SowExtensionPage';
import type { ReportLoad, SowExtensionRow } from '../../../../src/features/reports/types';

const ROWS: SowExtensionRow[] = [
  {
    employeeName: 'Aisha Thompson',
    employeeType: 'Full Time',
    clientName: 'Buckeye Mutual',
    extensionStartDate: '2026-01-05',
  },
  // No employee type -- must still render, with an empty Employee Type cell.
  {
    employeeName: 'Omar Haddad',
    employeeType: null,
    clientName: 'EDJE Internal',
    extensionStartDate: '2026-06-15',
  },
];

function loaded(rows: SowExtensionRow[]): ReportLoad<SowExtensionRow[]> {
  return { kind: 'loaded', value: rows };
}

function renderPage(overrides: Partial<Parameters<typeof SowExtensionPage>[0]> = {}) {
  const onRun = vi.fn();
  render(
    <SowExtensionPage
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
 * `{ selector: 'input' }` is load-bearing, matching `AssignmentStartReport.test.tsx`: `getByLabelText`
 * also matches an `aria-label`, and the Extension Start Date header carries "Sort by extension start
 * date" — so on a screen showing results, an unscoped `/start date/i` finds the input and that button.
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

describe('SowExtensionPage (issue #534)', () => {
  describe('report heading', () => {
    it('renders a "SOW Extension Report" heading', () => {
      renderPage();

      expect(
        screen.getByRole('heading', { level: 2, name: 'SOW Extension Report' }),
      ).toBeInTheDocument();
    });
  });

  describe('range validation — the query must not be issued for input the page rejects', () => {
    it('refuses an inverted range with a message and issues NO query', async () => {
      const { onRun } = renderPage();

      await runLookup('2026-03-31', '2026-03-01');

      expect(screen.getByText(INVERTED_RANGE_MESSAGE)).toBeInTheDocument();
      expect(onRun).not.toHaveBeenCalled();
    });

    it('accepts an equal start and end as a single-day range', async () => {
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
      // **This screen never had a screenshot baseline** — `reports.visual.spec.ts` covered
      // availability, assignment-duration and assignment-start only, so unlike its
      // `AssignmentStartReport` twin this case replaces nothing that #574 deleted. It is here because
      // the two screens carry the identical guard and the sibling gap was real, and because
      // `docs/TEST-STRATEGY.md` ruling 3 puts a validation-message render in Vitest anyway.
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
  });

  describe('results', () => {
    it('renders a row per extension with the requested columns', () => {
      renderPage({ report: loaded(ROWS) });

      expect(screen.getByRole('columnheader', { name: 'EDJEr Name' })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Employee Type' })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Client Name' })).toBeInTheDocument();
      expect(
        screen.getByRole('columnheader', { name: 'Extension Start Date' }),
      ).toBeInTheDocument();
      expect(screen.getByText('Aisha Thompson')).toBeInTheDocument();
      expect(screen.getByText('Omar Haddad')).toBeInTheDocument();
    });

    it('renders the employee type as its own column, following the name', () => {
      renderPage({ report: loaded(ROWS) });

      const row = screen.getByText('Aisha Thompson').closest('tr') as HTMLElement;
      const cells = within(row).getAllByRole('cell');

      // EDJEr Name, Employee Type, Client Name, Extension Start Date.
      expect(cells[1].textContent).toBe('Full Time');
    });

    it('renders an empty Employee Type cell when the EDJEr has none', () => {
      renderPage({ report: loaded(ROWS) });

      const row = screen.getByText('Omar Haddad').closest('tr') as HTMLElement;
      const cells = within(row).getAllByRole('cell');

      expect(cells[1].textContent).toBe('');
    });

    it('renders start dates as mm/dd/yyyy, never the raw ISO string', () => {
      renderPage({ report: loaded(ROWS) });

      expect(screen.getByText('01/05/2026')).toBeInTheDocument();
      expect(screen.queryByText('2026-01-05')).not.toBeInTheDocument();
    });

    it('shows an explicit empty state rather than a blank table', () => {
      renderPage({ report: loaded([]) });

      expect(screen.getByText('No extension SOWs started in that date range.')).toBeInTheDocument();
    });

    it('shows nothing at all before a lookup has been run', () => {
      renderPage();

      expect(
        screen.queryByText('No extension SOWs started in that date range.'),
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

describe('SowExtensionPage — sortable headers', () => {
  /**
   * Three rows whose three orderings are all DIFFERENT from one another, so no assertion below can
   * pass by accident against the ordering some other column would have produced. Mirrors
   * `AssignmentStartReport.test.tsx`'s fixture, including the year-boundary trap on the date column.
   */
  const SORT_ROWS: SowExtensionRow[] = [
    {
      employeeName: 'Zoe Alvarez',
      employeeType: null,
      clientName: 'Zenith Freight',
      extensionStartDate: '2025-12-31',
    },
    {
      employeeName: 'Aisha Thompson',
      employeeType: null,
      clientName: 'Marlowe Group',
      extensionStartDate: '2026-06-15',
    },
    {
      employeeName: 'Omar Haddad',
      employeeType: null,
      clientName: 'Ardent Mills',
      extensionStartDate: '2026-01-05',
    },
  ];

  async function clickColumn(label: string) {
    await userEvent.click(screen.getByRole('button', { name: `Sort by ${label.toLowerCase()}` }));
  }

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

  function names(): string[] {
    return bodyRows().map((cells) => cells[0]);
  }

  it('orders by extension start date ascending by default (oldest first, owner confirmation)', () => {
    renderPage({ report: loaded(SORT_ROWS) });

    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);
  });

  it('marks the ordered column with aria-sort and leaves the other two "none"', () => {
    renderPage({ report: loaded(SORT_ROWS) });

    const table = screen.getByRole('table');
    expect(
      within(table).getByRole('columnheader', { name: /Extension Start Date/ }),
    ).toHaveAttribute('aria-sort', 'ascending');

    for (const label of ['EDJEr Name', 'Client Name']) {
      expect(within(table).getByRole('columnheader', { name: new RegExp(label) })).toHaveAttribute(
        'aria-sort',
        'none',
      );
    }
  });

  it('sorts by EDJEr Name A→Z on the first click', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('EDJEr Name');

    expect(names()).toEqual(['Aisha Thompson', 'Omar Haddad', 'Zoe Alvarez']);
  });

  it('sorts by Client Name A→Z on the first click', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');

    expect(names()).toEqual(['Omar Haddad', 'Aisha Thompson', 'Zoe Alvarez']);
  });

  it('reverses the extension start date column on the first click and restores it on the second', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Extension Start Date');
    expect(names()).toEqual(['Aisha Thompson', 'Omar Haddad', 'Zoe Alvarez']);
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', {
        name: /Extension Start Date/,
      }),
    ).toHaveAttribute('aria-sort', 'descending');

    await clickColumn('Extension Start Date');
    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);
  });

  it('sorts without re-issuing the lookup', async () => {
    const { onRun } = renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');
    await clickColumn('Extension Start Date');

    expect(onRun).not.toHaveBeenCalled();
  });

  it('returns to the default order when a new lookup is run', async () => {
    renderPage({ report: loaded(SORT_ROWS) });

    await clickColumn('Client Name');
    expect(names()).toEqual(['Omar Haddad', 'Aisha Thompson', 'Zoe Alvarez']);

    await runLookup('2026-01-01', '2026-12-31');

    expect(names()).toEqual(['Zoe Alvarez', 'Omar Haddad', 'Aisha Thompson']);
    expect(
      within(screen.getByRole('table')).getByRole('columnheader', {
        name: /Extension Start Date/,
      }),
    ).toHaveAttribute('aria-sort', 'ascending');
  });
});
