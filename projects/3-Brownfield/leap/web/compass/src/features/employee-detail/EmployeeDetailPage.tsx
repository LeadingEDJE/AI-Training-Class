import { useState } from 'react';
import { Pager } from '../../components/pagination';
import { DEFAULT_PAGE_SIZE, paginate } from '../../components/pagination-options';
import { Alert, PageHeader, Panel, StackedRows, StatusPill, Toggle } from '../../components/ui';
import { buttonClassName, tableLinkClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';
import type { DirectReport, EmployeeDetail } from './types';

interface EmployeeDetailPageProps {
  detail: EmployeeDetail | null | undefined;
  isPending: boolean;
  isError: boolean;
  /**
   * Whether this viewer may start a new assignment (AC-1, AC-2, feature 006) — Compass Ops or Super
   * Admin. This is the only check performed (AC-44); the server trusts the client's role claim.
   */
  canManageAssignments?: boolean;
}

export function EmployeeDetailPage({
  detail,
  isPending,
  isError,
  canManageAssignments = false,
}: EmployeeDetailPageProps) {
  const [directReportsPage, setDirectReportsPage] = useState(1);
  const [showFormerReports, setShowFormerReports] = useState(false);

  if (isPending) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        <p role="status">Loading the EDJEr's record…</p>
      </main>
    );
  }

  if (isError) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        {/* `Alert` renders this in the brand-taupe tone, matching every other error state on
            this screen (`no-slate-palette.test.ts` covers the rest of the app). */}
        <Alert>That record could not be loaded.</Alert>
      </main>
    );
  }

  if (!detail) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        <p>That EDJEr was not found.</p>
      </main>
    );
  }

  // Issue #455: true once ANY report in the list is inactive — `isActive` is only ever `false` for
  // a viewer who is allowed to see the "Former" pill (issue #399), never merely omitted.
  const canSeeFormerDirectReports = detail.directReports.some(
    (report) => report.isActive !== undefined,
  );

  const visibleDirectReports =
    canSeeFormerDirectReports && showFormerReports
      ? detail.directReports
      : detail.directReports.filter((report) => report.isActive !== false);

  const directReportsView = paginate(visibleDirectReports, DEFAULT_PAGE_SIZE, directReportsPage);

  // A balanced half-and-half split: 12 reports read as 6-and-6, not 10-and-2. The `Pager` above
  // already handles anything past the page size, so this only decides how one page is laid out.
  const DIRECT_REPORTS_SPLIT = 10;
  const firstColumn =
    directReportsView.rows.length > DIRECT_REPORTS_SPLIT
      ? directReportsView.rows.slice(0, DIRECT_REPORTS_SPLIT)
      : directReportsView.rows;
  const secondColumn =
    directReportsView.rows.length > DIRECT_REPORTS_SPLIT
      ? directReportsView.rows.slice(DIRECT_REPORTS_SPLIT)
      : [];

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-6 px-6 py-8 text-brand-text">
      <PageHeader
        title={`${detail.firstName} ${detail.lastName}`}
        {...(detail.isActive === false && {
          status: <StatusPill tone="neutral" label="Former" />,
        })}
      />

      <Panel>
        <dl className="grid grid-cols-1 gap-x-8 gap-y-3 sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Hire Date</dt>
            <dd className="mt-1 text-sm">{formatDate(detail.hireDate)}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Employee Type</dt>
            <dd className="mt-1 text-sm">{detail.employeeType}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Coach</dt>
            <dd className="mt-1 text-sm">
              {/* The drill-in into the coach's OWN record (issue #245). `coach` and `coachId`
                  always arrive together, so checking `coach` alone is sufficient here. */}
              {detail.coach !== null && detail.coachId !== null ? (
                <a href={`/compass/team-directory/${detail.coachId}`} className={tableLinkClass}>
                  {detail.coach}
                </a>
              ) : (
                (detail.coach ?? '—')
              )}
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">State</dt>
            <dd className="mt-1 text-sm">{detail.state}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Delivery Team</dt>
            <dd className="mt-1 text-sm">{detail.isDeliveryTeam ? 'Yes' : 'No'}</dd>
          </div>
        </dl>
      </Panel>

      {detail.directReports.length > 0 && (
        <Panel>
          <section>
            <div className="flex flex-wrap items-center gap-3">
              <h2 className="text-lg font-semibold">Direct Reports</h2>
              {/* Issue #455: shown to every viewer, not only the "Former" pill (issue #399) audience —
                  it renders as a no-op switch for a baseline viewer rather than being withheld. */}
              {canSeeFormerDirectReports && (
                <>
                  <span aria-hidden="true" className="text-brand-gray-muted">
                    ·
                  </span>
                  <Toggle
                    label="Show former"
                    checked={showFormerReports}
                    onChange={(next) => {
                      setShowFormerReports(next);
                      setDirectReportsPage(1);
                    }}
                  />
                </>
              )}
            </div>
            {visibleDirectReports.length === 0 ? (
              <p className="mt-2 text-sm">No active direct reports.</p>
            ) : (
              <>
                <div className="mt-2 flex flex-col gap-x-6 text-sm sm:flex-row">
                  <DirectReportColumn reports={firstColumn} />
                  {secondColumn.length > 0 && <DirectReportColumn reports={secondColumn} />}
                </div>
                {/* `Pager` is only rendered once a second page exists (`totalPages > 1` internally),
                    so it is wrapped here in that same check before mounting. */}
                <div className="mt-3">
                  <Pager
                    firstIndex={directReportsView.firstIndex}
                    shownCount={directReportsView.rows.length}
                    totalCount={visibleDirectReports.length}
                    currentPage={directReportsView.currentPage}
                    totalPages={directReportsView.totalPages}
                    itemNoun={{ singular: 'direct report', plural: 'direct reports' }}
                    onPage={setDirectReportsPage}
                  />
                </div>
              </>
            )}
          </section>
        </Panel>
      )}

      {detail.timeTrackingSettings && (
        <Panel>
          <section>
            <h2 className="text-lg font-semibold">Time Tracking Settings</h2>
            <ul className="mt-2 space-y-1 text-sm">
              <li>
                Timesheet Required: {detail.timeTrackingSettings.timesheetRequired ? 'Yes' : 'No'}
              </li>
              <li>
                Can Submit Under 40: {detail.timeTrackingSettings.canSubmitUnder40 ? 'Yes' : 'No'}
              </li>
              <li>
                Include in Payroll: {detail.timeTrackingSettings.includeInPayroll ? 'Yes' : 'No'}
              </li>
            </ul>
          </section>
        </Panel>
      )}

      <Panel>
        <section>
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-lg font-semibold">Assignment History</h2>
            {canManageAssignments && (
              <a
                href={`/compass/team-directory/${detail.id}/assignments/new`}
                className={`ml-auto ${buttonClassName({ variant: 'primary', size: 'small' })}`}
              >
                New Assignment
              </a>
            )}
          </div>

          {detail.assignmentHistory.length === 0 ? (
            <p className="mt-2 text-sm">No assignments on record.</p>
          ) : (
            <div className="mt-2">
              {/* `StackedRows` was adopted here after the audit measured this panel stranding
                  content at narrow widths (PR #223) — the same defect its twin, the client record's
                  `CC-3` history, also had. */}
              <StackedRows
                caption="Assignment history"
                columns={['Client', 'Start', 'End']}
                rows={detail.assignmentHistory}
                rowKey={(assignment) => assignment.assignmentId}
                cellClassNames={['align-top', 'align-top', 'align-top']}
                cells={(assignment) => [
                  <>
                    <a
                      href={`/compass/client-directory/${assignment.clientId}`}
                      className={tableLinkClass}
                    >
                      {assignment.clientName}
                    </a>
                    {assignment.canViewAssignment === true && (
                      <>
                        {' · '}
                        <a
                          href={`/compass/team-directory/${detail.id}/assignments/${assignment.assignmentId}`}
                          className={`${tableLinkClass} text-xs`}
                        >
                          View assignment
                        </a>
                      </>
                    )}

                    {assignment.note && <p className="mt-1 text-xs">{assignment.note}</p>}

                    {assignment.sows && assignment.sows.length > 0 && (
                      <ul className="mt-2 space-y-1 text-xs">
                        {assignment.sows.map((sow) => (
                          <li key={`${sow.startDate}-${sow.endDate}`}>
                            <span>
                              SOW · {sow.sowType} · {formatDate(sow.startDate)} to{' '}
                              {formatDate(sow.endDate)}
                            </span>
                            {/* Kept as separate elements purely for the middot spacing between
                                them — a single joined string would need its own separator logic. */}
                            {sow.rateIncrease === true && <span> · Rate increase</span>}
                            {sow.note && <p className="mt-0.5">{sow.note}</p>}
                          </li>
                        ))}
                      </ul>
                    )}
                  </>,
                  formatDate(assignment.startDate),
                  assignment.endDate ? formatDate(assignment.endDate) : 'Current',
                ]}
              />
            </div>
          )}
        </section>
      </Panel>
    </main>
  );
}

function DirectReportColumn({ reports }: { reports: DirectReport[] }) {
  return (
    <ul className="flex-1 space-y-1">
      {reports.map((report) => (
        <li key={report.id}>
          <a href={`/compass/team-directory/${report.id}`} className={tableLinkClass}>
            {report.firstName} {report.lastName}
          </a>
          {report.isActive === false && (
            <span className="ml-2 inline-block align-middle">
              <StatusPill tone="neutral" label="Former" />
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}
