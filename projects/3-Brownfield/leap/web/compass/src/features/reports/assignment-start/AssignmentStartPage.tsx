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
 * Column headers, generated from `AssignmentStartSortColumn` (issue #386) — `Employee Type` is
 * included here too since every header on this screen sorts the same way.
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

const DEFAULT_SORT: Sort = { column: 'startDate', direction: 'ascending' };

/**
 * Which way each column sorts on every click, including the one that already orders the table
 * (issue #464) — `toggle` below reads this map unconditionally rather than only on arrival.
 */
const INITIAL_DIRECTION: Record<AssignmentStartSortColumn, 'ascending' | 'descending'> = {
  startDate: 'ascending',
  employee: 'ascending',
  client: 'ascending',
};

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

export const INVERTED_RANGE_MESSAGE = 'The start date must be on or before the end date.';

/** The message shown when either date is missing. */
export const MISSING_DATE_MESSAGE = 'Enter both a start date and an end date.';

export interface AssignmentStartRange {
  from: string;
  to: string;
}

interface AssignmentStartPageProps {
  /**
   * The lookup's result. `undefined` renders the same empty state as a loaded-but-empty result,
   * since both mean "no rows to show."
   */
  report: ReportLoad<AssignmentStartRow[]> | undefined;
  isPending: boolean;
  isError: boolean;
  onRun: (range: AssignmentStartRange) => void;
}

export function AssignmentStartPage({
  report,
  isPending,
  isError,
  onRun,
}: AssignmentStartPageProps) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [ranRange, setRanRange] = useState<AssignmentStartRange | null>(null);
  // The chosen order is reset inside `Results` itself on every new lookup, keeping the reset logic
  // next to the state it resets rather than split across two components.
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);

  const sorted = useMemo(
    () =>
      report?.kind === 'loaded'
        ? [...report.value].sort((a, b) => compare(a, b, sort.column, sort.direction))
        : [],
    [report, sort],
  );

  const toggle = (column: AssignmentStartSortColumn) =>
    setSort((current) =>
      current.column === column
        ? { column, direction: current.direction === 'ascending' ? 'descending' : 'ascending' }
        : { column, direction: INITIAL_DIRECTION[column] },
    );

  const column = (key: AssignmentStartSortColumn): TableColumn => ({
    label: COLUMN_LABELS[key],
    // Undefined means the column cannot sort at all, which `Table` renders with no aria-sort
    // attribute at all — the primitive has no other state to distinguish.
    sortDirection: sort.column === key ? sort.direction : undefined,
    onSort: () => toggle(key),
  });

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

    // A string comparison here is a shortcut that happens to work for `yyyy-MM-dd`; parsing to a
    // real date type would be the more correct form once a second date format needs supporting.
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
      title="Assignment Start"
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

          {/* The spacer exists only for narrow viewports, where `FormField`'s label wraps onto
              two lines and would otherwise push the Run button out of alignment with the inputs. */}
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
      caption="Assignments that started within the selected date range"
      columns={columns}
      emptyMessage="No assignments started in that date range."
      rows={rows}
      rowKey={(row, index) => `${row.employeeName}-${row.clientName}-${row.startDate}-${index}`}
      cells={(row) => [
        row.employeeName,
        <EmployeeTypeCell employeeType={row.employeeType} />,
        row.clientName,
        formatDate(row.startDate),
      ]}
    />
  );
}
