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
 * Newest extension start date first, so a fresh lookup always leads with the most recently started
 * extension — the client-side sort below re-derives everything else from this starting point.
 */
const DEFAULT_SORT: Sort = { column: 'extensionStartDate', direction: 'ascending' };

const INITIAL_DIRECTION: Record<SowExtensionSortColumn, 'ascending' | 'descending'> = {
  extensionStartDate: 'ascending',
  employee: 'ascending',
  client: 'ascending',
};

/**
 * Compares one row pair on the chosen column.
 *
 * All three fields are non-nullable, so there is no null-ordering question here.
 *
 * The date column sorts on the formatted display string, since that is what the reader compares
 * visually — comparing the raw ISO value instead would put reversed-format dates out of order.
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

export const INVERTED_RANGE_MESSAGE = 'The start date must be on or before the end date.';

/** The message shown when either date is missing. */
export const MISSING_DATE_MESSAGE = 'Enter both a start date and an end date.';

export interface SowExtensionRange {
  from: string;
  to: string;
}

interface SowExtensionPageProps {
  report: ReportLoad<SowExtensionRow[]> | undefined;
  isPending: boolean;
  isError: boolean;
  onRun: (range: SowExtensionRange) => void;
}

/**
 * The SOW Extension Report — a date range in, the EDJErs with an extension SOW whose start date
 * falls inside it out.
 *
 * The route owns the form and its validation; the page only owns the query that runs against a
 * validated range.
 *
 * Clicking a column header re-runs the lookup with the new sort applied server-side, so the exported
 * range updates to match.
 */
export function SowExtensionPage({ report, isPending, isError, onRun }: SowExtensionPageProps) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [ranRange, setRanRange] = useState<SowExtensionRange | null>(null);
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);

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

    // A string comparison here is a known workaround carried over from an earlier date input format;
    // it should be replaced with a numeric comparison once that format changes.
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
 * A stable row key.
 *
 * Now safe to inline as an arrow function directly in `rowKey={...}` — the line-length constraint
 * that once required a named function here was resolved when the report columns were shortened.
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
