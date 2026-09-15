import { useMemo, useState } from 'react';
import { Alert, Card, StackedRows, StatusPill, type TableColumn } from '../../../components/ui';
import type { ReportLoad } from '../../../lib/report-load';
import { EmployeeTypeCell } from '../EmployeeTypeCell';
import { ExportButton } from '../export/ExportButton';
import type { AssignmentDurationRow, DurationSortColumn } from './types';

interface AssignmentDurationPageProps {
  report?: ReportLoad<AssignmentDurationRow[]>;
  isPending: boolean;
  isError: boolean;
}

const INITIAL_DIRECTION: Record<DurationSortColumn, 'ascending' | 'descending'> = {
  duration: 'descending',
  employee: 'ascending',
  client: 'ascending',
  coach: 'ascending',
};

/**
 * Compares one row pair on the chosen column.
 *
 * **Duration compares `durationDisplay` lexically** — "10 yrs" sorts before "3 yrs", which matches the
 * mockup's own display order exactly (FR-030).
 *
 * **A null coach always sorts as `''`, before every name, regardless of direction** (owner decision
 * 2026-08-20): `coachName` is the only nullable sortable column, and treating it as the empty string
 * keeps its position fixed whichever way the column is reversed — matching Postgres's `NULLS FIRST`
 * default in both directions.
 */
function compare(
  a: AssignmentDurationRow,
  b: AssignmentDurationRow,
  column: DurationSortColumn,
  direction: 'ascending' | 'descending',
): number {
  const sign = direction === 'ascending' ? 1 : -1;

  if (column === 'duration') {
    return sign * (a.totalDays - b.totalDays);
  }

  if (column === 'coach') {
    if (a.coachName === null && b.coachName === null) return 0;

    if (a.coachName === null) return sign;
    if (b.coachName === null) return -sign;

    return sign * a.coachName.localeCompare(b.coachName);
  }

  const left = column === 'employee' ? a.employeeName : a.clientName;
  const right = column === 'employee' ? b.employeeName : b.clientName;
  return sign * left.localeCompare(right);
}

/**
 * The Client Assignment Duration report — mockup `#s-reports` RPT-4 (AC-39, #77).
 *
 * **One row per assignment, not per EDJEr–client pair** (FR-015): an EDJEr who left a client and
 * returned shows two separate rows, one per stay.
 *
 * **Paginated, like the team-directory screen it borrows its sort affordance from** (research D-10) —
 * sorting re-issues the query against the current page rather than re-ordering an already-materialised
 * list, so sorting a deep page can be visibly slower than sorting a shallow one.
 */
export function AssignmentDurationPage({
  report,
  isPending,
  isError,
}: AssignmentDurationPageProps) {
  const rows = report?.kind === 'loaded' ? report.value : undefined;
  const isRefused = report?.kind === 'refused';

  const [sort, setSort] = useState<{
    column: DurationSortColumn;
    direction: 'ascending' | 'descending';
  }>({ column: 'duration', direction: 'descending' });

  const sorted = useMemo(
    () => (rows ? [...rows].sort((a, b) => compare(a, b, sort.column, sort.direction)) : undefined),
    [rows, sort],
  );

  const toggle = (column: DurationSortColumn) =>
    setSort((current) =>
      current.column === column
        ? {
            column,
            direction: current.direction === 'ascending' ? 'descending' : 'ascending',
          }
        : { column, direction: INITIAL_DIRECTION[column] },
    );

  const column = (label: string, key: DurationSortColumn): TableColumn => ({
    label,
    sortDirection: sort.column === key ? sort.direction : undefined,
    onSort: () => toggle(key),
  });

  return (
    <div className="text-brand-text">
      {isPending && (
        <p role="status" className="text-sm">
          Loading the Client Assignment Duration report…
        </p>
      )}

      {isRefused && (
        <Alert>You do not have permission to view the Client Assignment Duration report.</Alert>
      )}

      {isError && !isRefused && (
        <Alert>The Client Assignment Duration report could not be loaded.</Alert>
      )}

      {sorted && (
        <Card
          title="Client Assignment Duration"
          // The mockups' count pill beside the title. StatusPill's label always reads "N EDJErs"
          // regardless of row grain, since duplicate EDJEr rows for the same pair are already merged
          // upstream before this component ever sees them.
          action={
            <span className="flex items-center gap-3">
              <StatusPill
                tone="neutral"
                label={`${sorted.length} ${sorted.length === 1 ? 'pairing' : 'pairings'}`}
              />
              <ExportButton
                path="/api/compass/reports/assignment-duration/export"
                fallbackFileName="compass-assignment-duration.csv"
                label="Export CSV"
              />
            </span>
          }
        >
          <StackedRows
            caption="Client assignment duration, longest tenure first"
            columns={[
              column('EDJEr', 'employee'),
              'Employee Type',
              column('Client', 'client'),
              column('Coach', 'coach'),
              column('Duration', 'duration'),
            ]}
            emptyMessage="No active client assignments."
            rows={sorted}
            // `employeeId` alone is the row identity here — `clientId` is appended only as a
            // human-readable suffix, since duplicate EDJEr rows across clients are merged upstream.
            rowKey={(row) => `${row.employeeId}-${row.clientId}`}
            cellClassNames={[undefined, undefined, undefined, undefined, 'tabular-nums']}
            cells={(row) => [
              row.employeeName,
              <EmployeeTypeCell employeeType={row.employeeType} />,
              row.clientName,
              row.coachName ?? '',
              row.durationDisplay,
            ]}
          />
        </Card>
      )}
    </div>
  );
}
