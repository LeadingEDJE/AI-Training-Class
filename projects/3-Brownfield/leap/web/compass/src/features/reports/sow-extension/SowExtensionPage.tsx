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
import type { ReportLoad, SowExtensionRow, SowExtensionSortColumn } from '../types';

/** Column headers — issue #534's answer to "same column naming conventions" as the other reports. */
const COLUMN_LABELS: Record<SowExtensionSortColumn, string> = {
  employee: 'EDJEr Name',
  client: 'Client Name',
  extensionStartDate: 'Extension Start Date',
};

interface Sort {
  column: SowExtensionSortColumn;
  direction: 'ascending' | 'descending';
}

/**
 * The order the screen opens each answer in, and the order it returns to on a new lookup.
 *
 * Oldest extension start date first — the owner's explicit answer for this report ("oldest to
 * newest") — matching the order the server already returns
 * (`CompassReportRepository.GetSowExtensionsAsync`).
 */
const DEFAULT_SORT: Sort = { column: 'extensionStartDate', direction: 'ascending' };

/**
 * Which way each column sorts when it is first chosen.
 *
 * Consulted only when ARRIVING at a column from another one — choosing the column that already orders
 * the table reverses it instead, matching `AssignmentStartPage`'s `toggle`.
 */
const INITIAL_DIRECTION: Record<SowExtensionSortColumn, 'ascending' | 'descending'> = {
  extensionStartDate: 'ascending',
  employee: 'ascending',
  client: 'ascending',
};

/**
 * Compares one row pair on the chosen column.
 *
 * **All three fields are non-nullable** (`SowExtensionRowDto` on the wire), so there is no null-ordering
 * question here, matching `AssignmentStartPage`'s `compare`.
 *
 * **The date compares the raw ISO `extensionStartDate`, never `formatDate(extensionStartDate)`** — see
 * the note on `SowExtensionRow.extensionStartDate` for why the formatted form sorts plausibly and
 * wrongly.
 */
function compare(
  a: SowExtensionRow,
  b: SowExtensionRow,
  column: SowExtensionSortColumn,
  direction: 'ascending' | 'descending',
): number {
  const sign = direction === 'ascending' ? 1 : -1;

  if (column === 'extensionStartDate') {
    return sign * a.extensionStartDate.localeCompare(b.extensionStartDate);
  }

  const left = column === 'employee' ? a.employeeName : a.clientName;
  const right = column === 'employee' ? b.employeeName : b.clientName;
  return sign * left.localeCompare(right);
}

/** The message shown when the range is inverted. Asserted verbatim by the unit tests. */
export const INVERTED_RANGE_MESSAGE = 'The start date must be on or before the end date.';

/** The message shown when either date is missing. */
export const MISSING_DATE_MESSAGE = 'Enter both a start date and an end date.';

export interface SowExtensionRange {
  from: string;
  to: string;
}

interface SowExtensionPageProps {
  /**
   * The lookup's result, or `undefined` before one has been run. `undefined` renders neither a table
   * nor an empty state — "no rows" and "you have not asked yet" are different things to say.
   */
  report: ReportLoad<SowExtensionRow[]> | undefined;
  isPending: boolean;
  isError: boolean;
  /**
   * Called with a VALIDATED range. The page never calls this for input it has rejected, matching
   * `AssignmentStartPage`'s contract.
   */
  onRun: (range: SowExtensionRange) => void;
}

/**
 * The SOW Extension Report (issue #534) — a date range in, the EDJErs with an extension SOW whose
 * start date falls inside it out.
 *
 * **The page owns the form and its validation; the route owns the query** — the same split
 * `AssignmentStartPage` uses and for the same reason: a bare page render (no `QueryClientProvider`) is
 * what its unit tests need for the validation branch that must NOT issue a query.
 *
 * **All three columns order the table, client-side.** The lookup has already returned the whole answer
 * for the chosen range, so a click re-orders rows the page is holding and leaves both the query and the
 * exported range untouched.
 */
export function SowExtensionPage({ report, isPending, isError, onRun }: SowExtensionPageProps) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [error, setError] = useState<string | null>(null);
  // The range that was actually RUN, not what is currently in the inputs — matching
  // `AssignmentStartPage`'s `ranRange` so a user editing a date without pressing Run does not get an
  // export of the range they are still typing.
  const [ranRange, setRanRange] = useState<SowExtensionRange | null>(null);
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);

  // Client-side, over rows already in hand — a copy, because `Array.prototype.sort` mutates in place
  // and `report.value` belongs to the query cache.
  const sorted = useMemo(
    () =>
      report?.kind === 'loaded'
        ? [...report.value].sort((a, b) => compare(a, b, sort.column, sort.direction))
        : [],
    [report, sort],
  );

  /** Choosing the ordered column again reverses it; choosing another starts at its own direction. */
  const toggle = (column: SowExtensionSortColumn) =>
    setSort((current) =>
      current.column === column
        ? { column, direction: current.direction === 'ascending' ? 'descending' : 'ascending' }
        : { column, direction: INITIAL_DIRECTION[column] },
    );

  const column = (key: SowExtensionSortColumn): TableColumn => ({
    label: COLUMN_LABELS[key],
    sortDirection: sort.column === key ? sort.direction : undefined,
    onSort: () => toggle(key),
  });

  // `Employee Type` is a plain string, not a `column(...)` -- it has no `SowExtensionSortColumn` member
  // and does not sort, matching every other Compass report's treatment of this column.
  const columns: TableColumn[] = [
    column('employee'),
    'Employee Type',
    column('client'),
    column('extensionStartDate'),
  ];

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (from === '' || to === '') {
      setError(MISSING_DATE_MESSAGE);
      return;
    }

    // A string comparison is correct here: both values are `yyyy-MM-dd` from a native date input,
    // whose lexical order IS its chronological order.
    if (from > to) {
      setError(INVERTED_RANGE_MESSAGE);
      return;
    }

    setError(null);
    setRanRange({ from, to });
    setSort(DEFAULT_SORT);
    onRun({ from, to });
  }

  return (
    <Card
      title="SOW Extension Report"
      // Export only once a lookup has RUN, matching `AssignmentStartPage`.
      action={
        ranRange !== null && report?.kind === 'loaded' ? (
          <ExportButton
            path={`/api/compass/reports/sow-extension/export?from=${ranRange.from}&to=${ranRange.to}`}
            fallbackFileName={`compass-sow-extension-${ranRange.from}-to-${ranRange.to}.csv`}
            label="Export CSV"
          />
        ) : undefined
      }
    >
      <div className="flex flex-col gap-6">
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

/**
 * A stable row key. A named function rather than an inline arrow so the template literal fits on one
 * line — `SowExtensionPage`'s longer field name (`extensionStartDate` vs. `startDate`) pushes the
 * inline form past the 100-column limit, and Prettier's wrap then puts the arrow and the template on
 * separate lines, which `no-raw-dates.test.ts` reads as a bare `{date}` opening a line rather than a
 * key identity.
 */
function sowExtensionRowKey(row: SowExtensionRow, index: number): string {
  return `${row.employeeName}-${row.clientName}-${row.extensionStartDate}-${index}`;
}

function Results({
  report,
  isError,
  rows,
  columns,
}: {
  report: ReportLoad<SowExtensionRow[]> | undefined;
  isError: boolean;
  rows: SowExtensionRow[];
  columns: TableColumn[];
}) {
  if (report === undefined) {
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
      caption="EDJErs with an extension SOW starting within the selected date range"
      columns={columns}
      emptyMessage="No extension SOWs started in that date range."
      rows={rows}
      rowKey={sowExtensionRowKey}
      cells={(row) => [
        row.employeeName,
        <EmployeeTypeCell employeeType={row.employeeType} />,
        row.clientName,
        formatDate(row.extensionStartDate),
      ]}
    />
  );
}
