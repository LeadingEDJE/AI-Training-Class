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
   * Admin. A usability affordance only (AC-44): the server refuses the write independently, so
   * hiding this button is not what enforces anything.
   */
  canManageAssignments?: boolean;
}

/**
 * The read-only employee detail (AC-8, AC-10, AC-11).
 *
 * **Renders only what the server sent, and branches on PRESENCE.** Every optional field in
 * `EmployeeDetail` was withheld deliberately, so a section whose data is absent must not appear at
 * all — rendering it with blank values would tell the viewer a section exists that they cannot see,
 * which is the disclosure FR-005 exists to prevent, one level up.
 *
 * **No edit control anywhere, for any viewer** (AC-8). The Super Admin's edit path is Stream 2's
 * configuration surface, reached on its own terms rather than from here.
 *
 * **No Email row, and the Coach cell is a drill-in, not text** (issue #245, superseding the Team
 * Directory's original mockup in the same direction that issue changed the directory itself). Email
 * stays on the wire (`EmployeeDetail.email`) — it is the display that was removed, not the field —
 * and the coach's name links into their own record via `coachId`, the sibling id the name alone
 * cannot carry. A "Direct Reports" panel lists every EDJEr who names this record's subject as THEIR
 * coach, the org-chart direction opposite `coachId`; it branches on presence rather than on an
 * entitlement, because most EDJErs are not coaches and an empty section on every record would be
 * noise. That list renders two columns and paginates with the SAME `paginate`/`Pager` pair the Team
 * Directory uses (owner request, follow-up to #245) — a coach's report count can run well past what
 * one unbroken column reads comfortably.
 *
 * **Direct Reports defaults to active-only, with a "Show former" switch for the audience that could
 * ever see one** (issue #455). That audience is exactly who the "Former" pill (issue #399) was
 * already gated to — the switch is withheld from anyone else, for whom it would be a no-op.
 */
export function EmployeeDetailPage({
  detail,
  isPending,
  isError,
  canManageAssignments = false,
}: EmployeeDetailPageProps) {
  // Called unconditionally, ahead of every early return below — React requires the same hooks in
  // the same order on every render, and the loading/error/not-found branches would otherwise skip
  // this call on some renders and not others.
  const [directReportsPage, setDirectReportsPage] = useState(1);
  // Issue #455: active-only is the default view; a viewer who can see former direct reports at all
  // gets a switch to reveal them. Starts false on every render — there is no "remember my last
  // toggle" requirement, and the owner's ask was specifically that the default stays active-only.
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
        {/* `Alert`, not a bare `text-red-700` paragraph: that class is Tailwind's own red ramp, which
            is off-palette here and is what `no-slate-palette.test.ts` guards the equivalent of. */}
        <Alert>That record could not be loaded.</Alert>
      </main>
    );
  }

  if (!detail) {
    // A baseline viewer requesting an inactive EDJEr receives a 404 by design (FR-021) — the same
    // answer a genuinely absent record gives, so this state must read as "not found" rather than
    // hinting that something exists but is off limits.
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        <p>That EDJEr was not found.</p>
      </main>
    );
  }

  // Issue #455: whether THIS viewer could ever receive a former direct report at all — the same
  // audience the "Former" pill (issue #399) is gated to. `isActive` is present on every report an
  // elevated viewer receives (true or false, never omitted — see `EdjerVisibility`/`DirectReportDto`
  // on the server), and absent on every report a baseline viewer receives, whose reports are already
  // all-active (BR-1). So presence alone tells us the audience without a second entitlement to wire
  // through from the route.
  const canSeeFormerDirectReports = detail.directReports.some(
    (report) => report.isActive !== undefined,
  );

  // Active-only by default (owner request, issue #455) — a viewer who cannot see former reports at
  // all already has an all-active list, so this filter is a no-op for them rather than a second gate.
  const visibleDirectReports =
    canSeeFormerDirectReports && showFormerReports
      ? detail.directReports
      : detail.directReports.filter((report) => report.isActive !== false);

  // A page and a two-column split past 10, both owner requests (issue #245 follow-up): a coach's
  // report count can run well past what one unbroken column reads comfortably, so this reuses the
  // exact `paginate` + `Pager` pair the Team Directory itself uses rather than inventing a second
  // pagination shape.
  const directReportsView = paginate(visibleDirectReports, DEFAULT_PAGE_SIZE, directReportsPage);

  // The split is a FIXED count — each column holds AT MOST 10 — not a balanced half-and-half:
  // 12 reports read as 10-and-2, not 6-and-6. Below the split, everything stays in one column — a
  // CSS grid would otherwise interleave items into a lopsided two-column shape for a small team.
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
      {/* `PageHeader`, not a hand-rolled `h1`: this page shipped its own at `text-2xl` against the
          primitive's `text-xl`, which is the two-title-sizes defect feature 008 already fixed on two
          other screens. The Former marker becomes a `StatusPill` for the same reason — it was a
          bespoke taupe chip standing in for the component that exists. */}
      <PageHeader
        title={`${detail.firstName} ${detail.lastName}`}
        {...(detail.isActive === false && {
          status: <StatusPill tone="neutral" label="Former" />,
        })}
      />

      <Panel>
        {/* No Email row (issue #245) — still on the wire (`EmployeeDetail.email`), just no longer
            displayed, matching the Team Directory's own removal. */}
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
              {/* The drill-in into the coach's OWN record (issue #245) — a name alone cannot be a
                  link, which is exactly what `coachId` carries. Always present together with
                  `coach`, but guarded on both since a link with no destination is worse than plain
                  text. */}
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
          {/* Here rather than in Time Tracking Settings below: that section is presence-gated on an
              entitlement this value does not share, and a baseline viewer receives it (FR-004). Read-only
              text, matching its siblings — AC-8 gives this screen no edit path for any viewer. */}
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Delivery Team</dt>
            <dd className="mt-1 text-sm">{detail.isDeliveryTeam ? 'Yes' : 'No'}</dd>
          </div>
        </dl>
      </Panel>

      {/* Issue #245: every EDJEr who lists this record's subject as their coach. Branches on
          PRESENCE the same way Time Tracking Settings does below — most EDJErs are not coaches, and
          an empty "Direct Reports" section on every one of their records would be noise the way an
          empty Time Tracking panel would be, even though this is a data absence rather than a
          disclosure withholding.

          The PRESENCE check stays against the raw, unfiltered list (issue #455): a coach whose whole
          team has left keeps the section — with the toggle available to reach them — rather than
          losing it the moment the active-only default has nothing to show (owner decision). */}
      {detail.directReports.length > 0 && (
        <Panel>
          <section>
            <div className="flex flex-wrap items-center gap-3">
              <h2 className="text-lg font-semibold">Direct Reports</h2>
              {/* Issue #455: restricted to the SAME audience the "Former" pill (issue #399) already
                  is — a baseline viewer's reports are already all-active (BR-1), so a switch here
                  would be a no-op for them and is withheld rather than shown inert. Inline next to the
                  heading, per the owner's "Direct Reports · [switch] Show former" layout. */}
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
                      // Otherwise a page position chosen against one filtered length could land past
                      // the end of the other — e.g. page 2 of an all-reports view with nothing on
                      // page 2 of the active-only one.
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
                {/* Two INDEPENDENT lists, not one CSS grid — a grid's row-major auto-flow would put
                    the 1st/3rd/5th report on the left and the 2nd/4th/6th on the right, which is the
                    interleaving the owner explicitly did not want. Each column is its own `<ul>` so
                    the first 10 fill the left one and only the overflow reaches the right, and the
                    second list is omitted entirely (rather than rendered empty) below the split. */}
                <div className="mt-2 flex flex-col gap-x-6 text-sm sm:flex-row">
                  <DirectReportColumn reports={firstColumn} />
                  {secondColumn.length > 0 && <DirectReportColumn reports={secondColumn} />}
                </div>
                {/* `Pager` draws its own Previous/Next only past one page (`totalPages > 1`), so this
                    is safe to render unconditionally — the range summary alone is what a single page
                    shows. Counted against the VISIBLE (filtered) set, not the raw one, so the range
                    summary never claims more than what is actually on screen to page through. */}
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

      {/* AC-10: present only when the server sent it, which is Super Admin only. */}
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
            {/* AC-1, AC-2: the entry point for starting a new engagement, from the EDJEr's own
                record — never a standalone nav destination (feature 006, FR-055a).

                `buttonClassName` primary, not the `bg-brand-green/15` tint this shipped with: that
                was a fourth green, visibly paler than every other primary action in Compass (owner
                request 2026-08-18). Sharing the helper is also what keeps it from drifting again —
                that is exactly why feature 008 exported it for a `Link` to wear. */}
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
              {/* `StackedRows`, not a hand-rolled table: below `md` each assignment becomes a
                  label-value card (T078, owner decision 2026-08-25). This panel did NOT strand — the
                  audit measured no hidden width here — so the driver is consistency with its twin, the
                  client record's `CC-3` history (PR #223; spec 010 records the two as parallel), rather
                  than a measured defect. StackedRows supplies the scroll region itself at >= `md`, so
                  the explicit `ScrollableRegion` this replaces is gone rather than nested. */}
              <StackedRows
                caption="Assignment history"
                columns={['Client', 'Start', 'End']}
                rows={detail.assignmentHistory}
                rowKey={(assignment) => assignment.assignmentId}
                // `align-top` on every cell, which is what the old `<tr className="align-top">` did.
                // It matters here and nowhere else in the card set: the first cell can carry a note
                // and a nested SOW list, so without it the two dates centre themselves against a tall
                // neighbour instead of lining up with the client name.
                cellClassNames={['align-top', 'align-top', 'align-top']}
                cells={(assignment) => [
                  <>
                    <a
                      href={`/compass/client-directory/${assignment.clientId}`}
                      className={tableLinkClass}
                    >
                      {assignment.clientName}
                    </a>
                    {/* AC-1, AC-2: the entry point into the assignment's own detail/SOWs screen,
                        nested under this EDJEr's record — distinct from the client-name link above,
                        which stays AC-8's link into the CLIENT's view. Gated on a SERVER-granted
                        entitlement (AC-16/FR-025), not a client-side role check — an affordance the
                        server withheld cannot be rendered from this payload at all. */}
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

                    {/* Elevated only — absent means withheld, not empty. */}
                    {assignment.note && <p className="mt-1 text-xs">{assignment.note}</p>}

                    {assignment.sows && assignment.sows.length > 0 && (
                      <ul className="mt-2 space-y-1 text-xs">
                        {assignment.sows.map((sow) => (
                          <li key={`${sow.startDate}-${sow.endDate}`}>
                            <span>
                              SOW · {sow.sowType} · {formatDate(sow.startDate)} to{' '}
                              {formatDate(sow.endDate)}
                            </span>
                            {/* Each of these is its own element rather than more text joined by
                                separators: a screen reader reads a run of middot-joined fragments as
                                one long line, and the note is prose that deserves its own. */}
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

/** One column of the Direct Reports split (issue #245 follow-up) — a plain list of name links. */
function DirectReportColumn({ reports }: { reports: DirectReport[] }) {
  return (
    <ul className="flex-1 space-y-1">
      {reports.map((report) => (
        <li key={report.id}>
          <a href={`/compass/team-directory/${report.id}`} className={tableLinkClass}>
            {report.firstName} {report.lastName}
          </a>
          {/* Elevated tiers only (issue #399) — a viewer who still sees former direct reports
              (issue #245) needs a way to tell them apart from current ones. `isActive` is omitted
              rather than `false` for a baseline viewer, so this must check `=== false`. */}
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
