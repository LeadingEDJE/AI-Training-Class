import { useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import {
  Alert,
  Button,
  Card,
  FormField,
  StackedRows,
  type TableColumn,
} from '../../../components/ui';
import { formatDate } from '../../../lib/date';
import { EmployeeTypeCell } from '../EmployeeTypeCell';
import { ExportButton } from '../export/ExportButton';
import type { AssignmentStartRow, AssignmentStartSortColumn, ReportLoad } from '../types';

/**
 * Column headers, byte-exact from mockup `#s-reports` RPT-5 (Principle X rule 3). `Employee Type`
 * (issue #386) is added separately in `columns` below — it is not sortable, so it has no
 * `AssignmentStartSortColumn` member and does not belong in this lookup.
 */
const COLUMN_LABELS: Record<AssignmentStartSortColumn, string> = {
  employee: 'EDJEr Name',
  client: 'Client Name',
  startDate: 'Assignment Start Date',
};

interface Sort {
  column: AssignmentStartSortColumn;
  direction: 'ascending' | 'descending';
}

/**
 * The order the screen opens each answer in, and the order it returns to on a new lookup.
 *
 * Start date ascending is the order the server already returns
 * (`CompassReportRepository.GetAssignmentStartsAsync`), so adding sorting changes nothing a reader —
 * or an existing test or E2E expectation — was relying on.
 */
const DEFAULT_SORT: Sort = { column: 'startDate', direction: 'ascending' };

/**
 * Which way each column sorts when it is first chosen.
 *
 * **Consulted only when ARRIVING at a column from another one.** Choosing the column that already
 * orders the table reverses it instead — see `toggle` below — so this map never decides what the
 * Assignment Start Date header does on a first click from the default order. That click reverses to
 * descending, which is what `AssignmentStartReport.test.tsx` asserts.
 */
const INITIAL_DIRECTION: Record<AssignmentStartSortColumn, 'ascending' | 'descending'> = {
  // Oldest assignment first, matching both the server's order and the beach breakdown's own default
  // for a start-date column (issue #464) — so returning to this column from a name column restores
  // the order the screen opened on.
  startDate: 'ascending',
  // Names read most naturally A→Z.
  employee: 'ascending',
  client: 'ascending',
};

/**
 * Compares one row pair on the chosen column.
 *
 * **All three fields are non-nullable** (`AssignmentStartRowDto` on the wire), so unlike the duration
 * report's coach column there is no null-ordering question to answer here — and no unreachable null
 * handling to carry.
 *
 * **The date compares the raw ISO `startDate`, never `formatDate(startDate)`** — see the note on
 * `AssignmentStartRow.startDate` for why the formatted form sorts plausibly and wrongly.
 */
function compare(
  a: AssignmentStartRow,
  b: AssignmentStartRow,
  column: AssignmentStartSortColumn,
  direction: 'ascending' | 'descending',
): number {
  const sign = direction === 'ascending' ? 1 : -1;

  if (column === 'startDate') {
    return sign * a.startDate.localeCompare(b.startDate);
  }

  const left = column === 'employee' ? a.employeeName : a.clientName;
  const right = column === 'employee' ? b.employeeName : b.clientName;
  return sign * left.localeCompare(right);
}

/** The message shown when the range is inverted. Asserted verbatim by the unit tests. */
export const INVERTED_RANGE_MESSAGE = 'The start date must be on or before the end date.';

/** The message shown when either date is missing. */
export const MISSING_DATE_MESSAGE = 'Enter both a start date and an end date.';

export interface AssignmentStartRange {
  from: string;
  to: string;
}

interface AssignmentStartPageProps {
  /**
   * The lookup's result, or `undefined` before one has been run. `undefined` is the untouched state
   * and renders neither a table nor an empty state — "no rows" and "you have not asked yet" are
   * different things to say, and collapsing them would tell a user their range matched nothing before
   * they picked one.
   */
  report: ReportLoad<AssignmentStartRow[]> | undefined;
  isPending: boolean;
  isError: boolean;
  /**
   * Called with a VALIDATED range. The page never calls this for input it has rejected — spec US4
   * scenario 2 requires that an inverted range issue no query at all, not merely that its result be
   * discarded.
   */
  onRun: (range: AssignmentStartRange) => void;
}

/**
 * The Assignment Start lookup (AC-40, RPT-5, issue #78) — a date range in, the assignments that
 * started inside it out.
 *
 * **The page owns the form and its validation; the route owns the query.** Every other Compass screen
 * that took a `useQuery` into a presentational component broke its own unit tests with
 * `No QueryClient set`, because these suites render pages bare (see `useInvoiceFrequencyOptions`).
 * Keeping the fetch in `AssignmentStartRoute` means the validation branch below — the half that must
 * NOT issue a query — is testable without a provider at all.
 *
 * **All three headers order the table (issue #461), client-side.** The lookup has already returned
 * the whole answer for the chosen range, so there is nothing server-side left to ask: a click
 * re-orders rows the page is holding and leaves both the query and the exported range untouched.
 */
export function AssignmentStartPage({
  report,
  isPending,
  isError,
  onRun,
}: AssignmentStartPageProps) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [error, setError] = useState<string | null>(null);
  // The range that was actually RUN, which is not the same as what is currently in the inputs: a user
  // who edits a date without pressing Run must not get an export of the range they are still typing.
  const [ranRange, setRanRange] = useState<AssignmentStartRange | null>(null);
  // The chosen order lives HERE rather than in `Results`, so that resetting it on a new lookup is an
  // explicit decision instead of a side effect of whether `Results` happens to unmount. It does
  // unmount while an uncached range is in flight (the page renders nothing at all then), but a re-run
  // of a range react-query still has cached never blanks the screen — so state held down there would
  // reset for one kind of new lookup and survive the other, for no reason a user could see.
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);

  // Client-side, over rows that are already in hand: re-ordering them is a presentation concern and
  // must not re-issue the lookup. The array is COPIED first because `Array.prototype.sort` mutates in
  // place and `report.value` belongs to the query cache — sorting it directly would reorder it for
  // every other reader of that cache entry.
  //
  // That sort is also stable, so rows tied on the ordered column keep the order they arrived in. For
  // the default that means the server's `EmployeeName` tiebreak survives without this comparator
  // restating it — a second copy of that decision could only drift from the first.
  const sorted = useMemo(
    () =>
      report?.kind === 'loaded'
        ? [...report.value].sort((a, b) => compare(a, b, sort.column, sort.direction))
        : [],
    [report, sort],
  );

  /** Choosing the ordered column again reverses it; choosing another starts at its own direction. */
  const toggle = (column: AssignmentStartSortColumn) =>
    setSort((current) =>
      current.column === column
        ? { column, direction: current.direction === 'ascending' ? 'descending' : 'ascending' }
        : { column, direction: INITIAL_DIRECTION[column] },
    );

  const column = (key: AssignmentStartSortColumn): TableColumn => ({
    label: COLUMN_LABELS[key],
    // Undefined means "not the ordered column", which `Table` renders as aria-sort="none" — among
    // sortable columns that reads as sortable-but-not-sorted, where omitting the attribute is what
    // the primitive reserves for a column that cannot sort at all.
    sortDirection: sort.column === key ? sort.direction : undefined,
    onSort: () => toggle(key),
  });

  // `Employee Type` (issue #386) is a plain string, not a `column(...)` -- it has no
  // `AssignmentStartSortColumn` member and does not sort, matching the non-sortable column
  // `AssignmentDurationPage.tsx` mixes into its own otherwise-sortable header list the same way.
  const columns: TableColumn[] = [
    column('employee'),
    'Employee Type',
    column('client'),
    column('startDate'),
  ];

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (from === '' || to === '') {
      setError(MISSING_DATE_MESSAGE);
      return;
    }

    // A string comparison is correct here and not a shortcut: both values are `yyyy-MM-dd` from a
    // native date input, a format whose lexical order IS its chronological order. Parsing to `Date`
    // would introduce a timezone the question does not have.
    if (from > to) {
      setError(INVERTED_RANGE_MESSAGE);
      return;
    }

    setError(null);
    setRanRange({ from, to });
    // A lookup is a distinct question, so its answer opens in the server's own order rather than in
    // whatever ordering the previous answer was left in. The alternative — carrying the chosen column
    // across — is defensible too, but it means the first thing a user sees after pressing Run is a set
    // of rows arranged by a caret they set against a different date range.
    setSort(DEFAULT_SORT);
    onRun({ from, to });
  }

  return (
    <Card
      title="Assignment Start"
      // Export only once a lookup has RUN. Before that there is no range to export, and offering the
      // control would say a file is available when the screen is still showing its empty form. An
      // empty result is still exportable — a header-only CSV is a valid answer to a valid question.
      action={
        ranRange !== null && report?.kind === 'loaded' ? (
          <ExportButton
            path={`/api/compass/reports/assignment-start/export?from=${ranRange.from}&to=${ranRange.to}`}
            fallbackFileName={`compass-assignment-start-${ranRange.from}-to-${ranRange.to}.csv`}
            label="Export CSV"
          />
        ) : undefined
      }
    >
      <div className="flex flex-col gap-6">
        {/* Mockup RPT-5 `.searchrow`: the two date fields and Run share one flex row, wrapping onto
            the next line on a narrow viewport. The fields take a fixed width on desktop (`sm:w-64`) so
            Run fits at the end of the row rather than being pushed to wrap; on mobile they go full
            width and Run wraps below.

            `items-start`, NOT `items-end`: the Start Date field grows DOWNWARD when a validation error
            appears under its input, and bottom-aligning the row to that taller field would drop End
            Date's input (and Run) below Start's, misaligning the two inputs. Top-aligning keeps both
            inputs on one line regardless of the error; Run is given an invisible, label-height spacer
            below so it still lines up with the inputs rather than the labels. */}
        <form onSubmit={handleSubmit} noValidate className="flex flex-wrap items-start gap-4">
          <div className="w-full sm:w-64">
            <FormField label="Start Date" required error={error ?? undefined}>
              {(id) => (
                <input
                  id={id}
                  type="date"
                  value={from}
                  onChange={(event) => setFrom(event.target.value)}
                  className="w-full rounded border border-brand-gray/70 bg-white px-2 py-1.5 text-sm"
                />
              )}
            </FormField>
          </div>

          <div className="w-full sm:w-64">
            <FormField label="End Date" required>
              {(id) => (
                <input
                  id={id}
                  type="date"
                  value={to}
                  onChange={(event) => setTo(event.target.value)}
                  className="w-full rounded border border-brand-gray/70 bg-white px-2 py-1.5 text-sm"
                />
              )}
            </FormField>
          </div>

          {/* The spacer mirrors a `FormField`'s label (`text-sm font-medium`, one line) and its
              `gap-1`, so with `items-start` the Run button lands at the same height as the inputs and
              stays there when the Start Date error grows its field. */}
          <div className="flex flex-col gap-1">
            <span aria-hidden="true" className="invisible text-sm font-medium">
              Run
            </span>
            <Button type="submit">Run</Button>
          </div>
        </form>

        {isPending && report === undefined ? null : (
          <Results report={report} isError={isError} rows={sorted} columns={columns} />
        )}
      </div>
    </Card>
  );
}

function Results({
  report,
  isError,
  rows,
  columns,
}: {
  /** What to SAY: which of the four states the lookup is in. */
  report: ReportLoad<AssignmentStartRow[]> | undefined;
  isError: boolean;
  /** What to SHOW: the loaded rows in the chosen order. Empty on every non-loaded state. */
  rows: AssignmentStartRow[];
  /** The sortable headers, owned by the page because the order they reflect is the page's state. */
  columns: TableColumn[];
}) {
  if (report === undefined) {
    // Nothing has been asked yet. Deliberately silent rather than an empty table.
    return null;
  }

  if (isError) {
    return <Alert>The lookup could not be run. Try again.</Alert>;
  }

  if (report.kind === 'refused') {
    return <Alert>You do not have access to this report.</Alert>;
  }

  if (report.kind !== 'loaded') {
    return <Alert>The lookup could not be run. Try again.</Alert>;
  }

  return (
    <StackedRows
      caption="Assignments that started within the selected date range"
      columns={columns}
      emptyMessage="No assignments started in that date range."
      rows={rows}
      rowKey={(row, index) => `${row.employeeName}-${row.clientName}-${row.startDate}-${index}`}
      cells={(row) => [
        row.employeeName,
        <EmployeeTypeCell employeeType={row.employeeType} />,
        row.clientName,
        // mm/dd/yyyy through the shared helper (issue #234) -- never the raw ISO string.
        formatDate(row.startDate),
      ]}
    />
  );
}
