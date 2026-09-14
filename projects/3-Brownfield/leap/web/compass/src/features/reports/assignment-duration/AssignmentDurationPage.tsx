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

/** Which way each column sorts when it is first chosen. */
const INITIAL_DIRECTION: Record<DurationSortColumn, 'ascending' | 'descending'> = {
  // Longest tenure first, matching the mockup's descending caret on Duration (FR-014, RPT-4).
  duration: 'descending',
  // Names read most naturally A→Z, so a first click on a name column ascends.
  employee: 'ascending',
  client: 'ascending',
  coach: 'ascending',
};

/**
 * Compares one row pair on the chosen column.
 *
 * **Duration compares `totalDays`, never `durationDisplay`.** Lexically "10 yrs" precedes "3 yrs",
 * which is wrong and looks entirely plausible — the trap FR-030 names explicitly.
 *
 * **A null coach sorts AFTER every name, and therefore flips with the direction** (owner decision
 * 2026-08-20): A→Z puts the no-coach rows at the END, Z→A puts them at the START. `coachName` is the
 * only nullable sortable column and nothing upstream said where nulls land, so it is decided here and
 * asserted both ways.
 *
 * This replaced pinning nulls last in both directions. Pinning is defensible in isolation — absence is
 * not a name — but it makes reversing the column a partial reversal, so the list a user sees after two
 * clicks is not the reverse of the list they saw after one, and the rows that did not move are the ones
 * with nothing in the cell to explain why. Sorting null as "after Z" keeps the reversal total and
 * matches what a reader already expects from `NULLS LAST` on ascending / `NULLS FIRST` on descending,
 * which is Postgres's own default for the same shape.
 *
 * Still not `''`: an empty string sorts into the alphabet BEFORE "A", which puts absent coaches at the
 * head of an A→Z list among real names and makes them look like data.
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
    // Two absent coaches are equal, so neither jumps the other when the direction flips.
    if (a.coachName === null && b.coachName === null) return 0;

    // `sign`, not a fixed 1/-1: null ranks after every name, and the direction carries it. A→Z
    // (`sign` 1) sends it to the end; Z→A (`sign` -1) brings it to the front.
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
 * **Five columns: EDJEr, Employee Type, Client, Coach, Duration.** The first four are named by FR-014,
 * AC-39, Journey Map J20, research D-4, the read-surface contract and checklist item CHK019; Employee
 * Type was added as its own column by issue #386, matching the CSV export's column set. Coach is not
 * optional decoration; an earlier revision of this phase's tasks omitted it from every build step,
 * which would have been caught only at T102 in the last phase of the feature.
 *
 * **One row per EDJEr–client PAIR, not per assignment** (FR-015). An EDJEr who left a client and
 * returned shows their combined tenure in one row, so two rows never name the same pair.
 *
 * **Single page — deliberately no pagination.** The team-directory screen this borrows its sort
 * affordance from also paginates, and that was an owner request for that screen rather than a house
 * default (research D-10).
 *
 * Sorting is client-side over an already-materialised list: the server returns the full report ordered
 * longest-first, and re-sorting is a presentation concern that must not re-issue the query — FR-014 says
 * "the ordering changes and the data set is unchanged".
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

  /** Choosing the ordered column again reverses it; choosing another starts at its natural direction. */
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
    // Undefined here means "not the ordered column", and `Table` renders that as aria-sort="none" —
    // which is correct: among sortable columns, "none" says sortable-but-not-sorted, while omitting the
    // attribute is what the primitive reserves for a column that cannot sort at all.
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
          // The mockups' count pill beside the title. A WORD, not a bare number: StatusPill has no
          // colour-only form, and "12 pairings" says what is being counted — the row grain is the
          // EDJEr-client pair, so "12 EDJErs" would be wrong wherever anyone works two clients.
          action={
            // The pill and the export share the one action slot. The pill stays FIRST so the count
            // keeps its place beside the title (the mockup's layout); the export is the control the
            // user reaches for, so it sits at the outer edge.
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
              // A plain string header, not `column(...)`: `DurationSortColumn` has no `employeeType`
              // member, and sorting by type is not in scope — `TableColumn` accepts either.
              'Employee Type',
              column('Client', 'client'),
              column('Coach', 'coach'),
              column('Duration', 'duration'),
            ]}
            emptyMessage="No active client assignments."
            rows={sorted}
            // The PAIR is the row identity. `employeeId` alone repeats when an EDJEr works two
            // clients, and `clientId` alone repeats across EDJErs.
            rowKey={(row) => `${row.employeeId}-${row.clientId}`}
            cellClassNames={[undefined, undefined, undefined, undefined, 'tabular-nums']}
            cells={(row) => [
              row.employeeName,
              // Its own column, following the name (issue #386).
              <EmployeeTypeCell employeeType={row.employeeType} />,
              row.clientName,
              // An empty cell, never a filtered row (spec Edge Cases).
              row.coachName ?? '',
              row.durationDisplay,
            ]}
          />
        </Card>
      )}
    </div>
  );
}
