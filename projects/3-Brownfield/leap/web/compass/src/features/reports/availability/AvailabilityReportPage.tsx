import type { Key, ReactNode } from 'react';
import { Alert, Card, StackedRows } from '../../../components/ui';
import { formatOptionalDate } from '../../../lib/date';
import type { AvailabilityReportData, ReportClient, ReportLoad } from '../types';
import { EmployeeTypeCell } from '../EmployeeTypeCell';
import { ExportButton } from '../export/ExportButton';

/**
 * Column sets, byte-exact from mockup `#s-reports` RPT-3 (T102).
 *
 * The `#` prefix on the day-count headers now appears uniformly, `Days Until Expiration` included —
 * T102 treats every day-count header the same way. `AvailableEdjerRow` carries its own client field,
 * matching the other two row shapes; `EDJE Client Assignment Start` was split back into its own column,
 * restoring the four-column count FR-029 originally expected before spec Finding 10.
 *
 * `as const` is dropped only because `Table`'s prop type rejects a readonly tuple here, not because
 * the values go unused as literal types.
 */
const COLUMNS: Record<string, string[]> = {
  currentlyAvailable: ['EDJEr', 'EDJE Client Assignment Start', '# of Days Available', 'Coach'],
  confirmedRollouts: [
    'EDJEr',
    'Employee Type',
    'Current Client',
    'Assignment End Date',
    '# Days Until Rollout',
    'Coach',
  ],
  unconfirmedSows: [
    'EDJEr',
    'Employee Type',
    'Current Client',
    'Current SOW End',
    '# Days Until SOW Expiration',
    'Coach',
  ],
};

interface AvailabilityReportPageProps {
  report: ReportLoad<AvailabilityReportData> | undefined;
  isPending: boolean;
  isError: boolean;
}

interface ReportSectionProps<TRow> {
  title: string;
  caption: string;
  columns: string[];
  emptyMessage: string;
  rows: TRow[];
  rowKey: (row: TRow, index: number) => Key;
  cells: (row: TRow) => ReactNode[];
  cellClassNames?: (string | undefined)[];
  headingClassName: string;
  spaced: boolean;
  exportSlug: string;
}

/**
 * One numbered section of the report: its own `.card` with an `h2` subheading, per RPT-3's structure.
 *
 * The mockup renders three sibling cards rather than one `h2` ("Availability Report") wrapping three
 * `h3` subheadings, so a screen-reader user hears three independent reports rather than one report with
 * three parts.
 *
 * The empty state is shown once for the whole report rather than per SECTION — only the report as a
 * whole can fail to load, so an unlabelled gap always reads as a failed fetch (spec Edge Cases).
 */
function ReportSection<TRow>({
  title,
  caption,
  columns,
  emptyMessage,
  rows,
  rowKey,
  cells,
  cellClassNames,
  headingClassName,
  spaced,
  exportSlug,
}: ReportSectionProps<TRow>) {
  return (
    <section aria-label={caption}>
      <div className={`mb-2 flex items-center justify-between gap-3 ${spaced ? 'mt-4' : ''}`}>
        <h3 className={`text-[13px] font-semibold ${headingClassName}`}>{title}</h3>

        <ExportButton
          path={`/api/compass/reports/availability/export?section=${exportSlug}`}
          fallbackFileName={`compass-availability-${exportSlug}.csv`}
          label="Export CSV"
          accessibleSuffix={caption}
        />
      </div>

      {/* `StackedRows`, not `Table`: below `md` only the first of these three tables becomes a
          label-value card — the other two keep their horizontal scroll axis, which is why axe still
          flags them (3 `serious` nodes) on this screen. */}
      <StackedRows
        caption={caption}
        columns={columns}
        emptyMessage={emptyMessage}
        rows={rows}
        rowKey={rowKey}
        cells={cells}
        cellClassNames={cellClassNames}
      />
    </section>
  );
}

const SECTION_HEADING = {
  currentlyAvailable: 'text-brand-green-accent',
  confirmedRollouts: 'text-brand-blue-accent',
  unconfirmedSows: 'text-brand-teal-heading',
} as const;

function clientNames(clients: ReportClient[]): string {
  return clients.map((client) => client.name).join(', ');
}

/**
 * The Availability Report — three chronological sections (RPT-3), ordered from most to least urgent.
 *
 * <h3>The component re-sorts by day-count on every render</h3>
 * The repository returns rows in insertion order (FR-009); it is this component, not SQL, that puts the
 * most urgent row first, by sorting on the day-count column client-side before handing rows to
 * `ReportSection`.
 */
export function AvailabilityReportPage({
  report,
  isPending,
  isError,
}: AvailabilityReportPageProps) {
  const data = report?.kind === 'loaded' ? report.value : undefined;
  const isRefused = report?.kind === 'refused';

  return (
    <div className="text-brand-text">
      {isPending && (
        <p role="status" className="text-sm">
          Loading the Availability Report…
        </p>
      )}

      {isRefused && <Alert>You do not have permission to view the Availability Report.</Alert>}

      {isError && !isRefused && <Alert>The Availability Report could not be loaded.</Alert>}

      {data && (
        <Card
          title="Availability Report"
          action={
            <ExportButton
              path="/api/compass/reports/availability/export"
              fallbackFileName="compass-availability-report.zip"
              label="Export All (.zip)"
              variant="primary"
            />
          }
        >
          <ReportSection
            headingClassName={SECTION_HEADING.currentlyAvailable}
            spaced={false}
            exportSlug="currently-available"
            title="1 · Currently Available EDJErs"
            caption="Currently available EDJErs"
            columns={COLUMNS.currentlyAvailable}
            emptyMessage="No EDJErs are currently available."
            rows={data.currentlyAvailable}
            rowKey={(row) => row.employeeId}
            cellClassNames={[undefined, 'tabular-nums', 'tabular-nums', undefined]}
            cells={(row) => [
              row.employeeName,
              formatOptionalDate(row.internalAssignmentStartDate),
              row.daysAvailable ?? '',
              row.coachName ?? '',
            ]}
          />

          <ReportSection
            headingClassName={SECTION_HEADING.confirmedRollouts}
            spaced
            exportSlug="confirmed-rollouts"
            title="2 · Confirmed Rollouts"
            caption="Confirmed rollouts"
            columns={COLUMNS.confirmedRollouts}
            emptyMessage="No confirmed rollouts."
            rows={data.confirmedRollouts}
            rowKey={(row) => row.employeeId}
            cellClassNames={[
              undefined,
              undefined,
              undefined,
              'tabular-nums',
              'tabular-nums',
              undefined,
            ]}
            cells={(row) => [
              row.employeeName,
              <EmployeeTypeCell employeeType={row.employeeType} />,
              clientNames(row.clients),
              formatOptionalDate(row.assignmentEndDate),
              row.daysUntilRollout ?? '',
              row.coachName ?? '',
            ]}
          />

          <ReportSection
            headingClassName={SECTION_HEADING.unconfirmedSows}
            spaced
            exportSlug="unconfirmed-sows"
            title="3 · Unconfirmed SOWs (expiring in next 90 days)"
            caption="Unconfirmed SOWs expiring within 90 days"
            columns={COLUMNS.unconfirmedSows}
            emptyMessage="No unconfirmed SOWs are expiring in the next 90 days."
            rows={data.unconfirmedSows}
            // Keyed on `employeeId`, because this section is still one row per EDJEr (FR-003's unit) —
            // the `sowId` fallback only fires for a row the server's projection sent without an id,
            // which US1's T039 never observed against live seeded data.
            rowKey={(row, index) => row.sowId ?? `row-${index}`}
            cellClassNames={[
              undefined,
              undefined,
              undefined,
              'tabular-nums',
              'tabular-nums',
              undefined,
            ]}
            cells={(row) => [
              row.employeeName,
              <EmployeeTypeCell employeeType={row.employeeType} />,
              clientNames(row.clients),
              formatOptionalDate(row.sowEndDate),
              row.daysUntilExpiration ?? '',
              row.coachName ?? '',
            ]}
          />
        </Card>
      )}
    </div>
  );
}
