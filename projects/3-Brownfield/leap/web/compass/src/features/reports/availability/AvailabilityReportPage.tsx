import type { Key, ReactNode } from 'react';
import { Alert, Card, StackedRows } from '../../../components/ui';
import { formatOptionalDate } from '../../../lib/date';
import type { AvailabilityReportData, ReportClient, ReportLoad } from '../types';
import { EmployeeTypeCell } from '../EmployeeTypeCell';
import { ExportButton } from '../export/ExportButton';

/**
 * Column sets, byte-exact from mockup `#s-reports` RPT-3 (Principle X rule 3, T102), plus the
 * owner-requested additions from issue #334 (spec Deviation 11): `# of Days Available` on §1, and
 * employee type as its own column on §2/§3 (originally rendered inline in the `EDJEr` cell per issue
 * #334; moved to its own column by issue #386 to line up with the CSV export, which already treated it
 * as a separate field).
 *
 * **The `#` prefix on the day-count headers is the mockup's and is preserved on the original two**, and
 * it is deliberately ABSENT from the dashboard's `Days Until Expiration` — T102 asserts that
 * distinction, so do not "normalise" the two screens to one spelling. Issue #334 specified
 * `# of Days Available` verbatim for the new §1 column, which does carry the prefix.
 *
 * §1 was **three** columns and is now four. `EDJE Client Assignment Start` is one date column, not a
 * client column plus a date; FR-029 once read it as four for that reason and that was a misread (spec
 * Finding 10, T050) — `# of Days Available` is a different, later addition and not a relapse of that
 * misread. `AvailableEdjerRow` still has no client field.
 *
 * Plain `string[]`, not `as const`: the values are never used as literal types, and a readonly tuple
 * only forces every call site to copy it into a fresh array to satisfy `Table`'s prop.
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
  /** The subheading's colour class — RPT-3 gives each section its own (see {@link SECTION_HEADING}). */
  headingClassName: string;
  /** False on the first section: RPT-3 leads with `margin:8px 0`, then `16px 0 8px` for the rest. */
  spaced: boolean;
  /**
   * The kebab-case section name the export route takes. Each section exports SEPARATELY because the
   * three do not share a column set (four columns, then six and six) — a merged CSV would have to drop
   * columns or invent them, which is the same reason `AvailabilityReportDto` keeps three typed row
   * shapes rather than one.
   */
  exportSlug: string;
}

/**
 * One numbered section of the report: an `h3` subheading over its table, or the section's empty state.
 *
 * <h3>A subheading, not a card — this is RPT-3's structure</h3>
 * The mockup renders the whole Availability Report as ONE `.card` with an `h2` ("Availability Report")
 * and three `h3` subheadings inside it. Three separate cards, each titling itself `h2`, was the earlier
 * shape here: it gave the screen no report-level heading at all and promoted the sections a level, so a
 * screen-reader user heard three sibling reports rather than one report with three parts.
 *
 * The empty state stays per SECTION — one section can legitimately be empty while the other two have
 * rows, and an unlabelled gap reads as a failed fetch (spec Edge Cases).
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
      {/* 13px and the section's own colour, per RPT-3. `mt-4` reproduces the mockup's `16px 0 8px` on
          the second and third; the first uses its `8px 0`. */}
      <div className={`mb-2 flex items-center justify-between gap-3 ${spaced ? 'mt-4' : ''}`}>
        <h3 className={`text-[13px] font-semibold ${headingClassName}`}>{title}</h3>

        {/* The section's own export, beside its heading rather than in a shared toolbar: with three of
            them on one screen, adjacency is what tells you which button exports which table. `caption`
            becomes the screen-reader suffix so the three do not all announce as plain "Export CSV". */}
        <ExportButton
          path={`/api/compass/reports/availability/export?section=${exportSlug}`}
          fallbackFileName={`compass-availability-${exportSlug}.csv`}
          label="Export CSV"
          accessibleSuffix={caption}
        />
      </div>

      {/* `StackedRows`, not `Table`: below `md` each row becomes a label-value card, so none of these
          three tables has an off-screen axis to strand content on. All three did — 134/374/396px, none
          with a keyboard path, and this screen was the only one axe also caught (3 `serious` nodes). */}
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

/**
 * The subheading colour per section, following RPT-3's colour-coding.
 *
 * **Every one of the mockup's own three values fails WCAG AA at the 13px it sets**, so each is darkened
 * until it passes and the departure is recorded (Principle X rule 2): `--green-dark` #7FAE2C is 2.63:1,
 * `--le-blue` #5C95FF is 2.92:1, and #2596b3 is 3.45:1, against a 4.5:1 minimum for normal text. §2's
 * replacement is the pre-existing `brand-blue-accent` (5.44:1); §1 and §3 needed new tokens.
 * `brand-teal-accent` is NOT reused for §3 — it is the dashboard's large-text tile accent at 4.16:1.
 * Measured in `brand-contrast.test.ts`.
 */
const SECTION_HEADING = {
  currentlyAvailable: 'text-brand-green-accent',
  confirmedRollouts: 'text-brand-blue-accent',
  unconfirmedSows: 'text-brand-teal-heading',
} as const;

/**
 * Every client a row names, not just the first. A §2 row is one per EDJEr and two assignments can tie
 * on the latest end date, so naming one and dropping the other would hide a client from a sales screen.
 */
function clientNames(clients: ReportClient[]): string {
  return clients.map((client) => client.name).join(', ');
}

/**
 * A date cell, empty when the date is absent.
 *
 * **Absent is rendered as empty, never as today.** These sections are read for triage: a missing SOW end
 * date defaulted to the business date would render "expires today, 0 days" — the most urgent row the
 * screen can show — for data it does not have, and would sort to the top.
 */
/**
 * The Availability Report — three chronological sections (AC-38, FR-009 to FR-013; J19; RPT-3).
 *
 * <h3>Earliest date first, and the order is the server's</h3>
 * The repository orders in SQL on the source date column (FR-009). This component does not re-sort — a
 * second ordering in the client is a second place for the direction to drift.
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
          // "Export All" is the report-level action, and the primary one: it turns three separate
          // downloads into one click. A zip rather than a merged CSV, for the column-set reason above.
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
            // One row per EDJEr (FR-010), so the employee id is unique within this section.
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
            // One row per EDJEr (FR-011) — the tie on the latest end date is folded server-side.
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
            // Keyed on the SOW, because this section is one row per SOW (FR-003's unit) — an EDJEr with
            // two exposed SOWs appears twice, so `employeeId` is not unique here, and US1's T039 found
            // that exact duplicate-key defect on the dashboard against live seeded data. The index
            // fallback covers only a row the server sent without an id, which its projection does not
            // produce.
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
