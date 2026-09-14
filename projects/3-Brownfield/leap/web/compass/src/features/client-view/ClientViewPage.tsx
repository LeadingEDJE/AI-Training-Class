import { formatDate, formatOptionalDate } from '../../lib/date';
import type { ClientAssignmentHistoryRow, ClientView } from './types';
import { Alert, PageHeader, Panel, StackedRows, StatusPill } from '../../components/ui';
import { buttonClassName, tableLinkClass } from '../../components/ui-classes';

/**
 * Splits a client's assignment history into Current and Former (issue #658), sorting Former by end
 * date descending so the most recently-ended assignment shows first. Current keeps whatever order it
 * arrived in — the server already orders the whole list newest-start-first.
 *
 * **Buckets on the server-derived `status`, never on the dates.** The business date and the
 * inclusive end-date boundary are the server's call (BR-7, AC-42) — recomputing "current" from
 * `endDate` in the browser is exactly the comparison independent implementations get wrong.
 */
function splitAssignmentHistory(rows: ClientAssignmentHistoryRow[]) {
  const current = rows.filter((row) => row.status === 'Active');
  const former = rows
    .filter((row) => row.status === 'Inactive')
    // `Inactive` is derived from a past end date (BR-7), so it is always present here — the cast
    // narrows a type that is nullable for the row in general, not a fallback for a real gap.
    .sort((a, b) => (b.endDate as string).localeCompare(a.endDate as string));

  return { current, former };
}

interface ClientViewPageProps {
  view: ClientView | null | undefined;
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
 * The client view (AC-13, AC-14, AC-15, AC-16).
 *
 * **The client name is rendered outside — and above — every optional panel.** That is AC-13, and it
 * exists because of AC-14: the Client Details panel is hidden from four of the five tiers, so a name
 * placed inside it would leave those viewers looking at an unlabelled page. Moving the heading into
 * a panel would satisfy every other test here and silently break the one criterion that was written
 * to prevent exactly that.
 */
export function ClientViewPage({
  view,
  isPending,
  isError,
  canManageAssignments = false,
}: ClientViewPageProps) {
  if (isPending) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        <p role="status">Loading the client…</p>
      </main>
    );
  }

  if (isError) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        <Alert>That client could not be loaded.</Alert>
      </main>
    );
  }

  if (!view) {
    return (
      <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
        <p>That client was not found.</p>
      </main>
    );
  }

  const { current, former } = splitAssignmentHistory(view.assignmentHistory);

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-6 px-6 py-8 text-brand-text">
      {/* AC-13 — first, outside every panel, for every tier. The status rides beside the title as a
          StatusPill, which states it as a WORD rather than by colour alone (AC-NFR-5). */}
      <PageHeader
        title={view.clientName}
        status={
          <StatusPill
            tone={view.status === 'Active' ? 'affirmative' : 'neutral'}
            label={view.status}
          />
        }
      />

      {/* AC-14 — present only when the server sent it, which is Super Admin only. */}
      {view.clientDetails && (
        <Panel>
          <section>
            <h2 className="text-lg font-semibold">Client Details</h2>
            <dl className="mt-2 grid grid-cols-1 gap-x-8 gap-y-2 text-sm sm:grid-cols-2">
              {/* Issue #243: an internal (EDJE-to-EDJE) client has no MSA, no NDA, and nothing to
                  invoice — those fields have no meaning for one and must not display at all. */}
              {!view.clientDetails.isInternal && (
                <>
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide">Date MSA Signed</dt>
                    <dd>
                      {view.clientDetails.msaSignedDate
                        ? formatDate(view.clientDetails.msaSignedDate)
                        : '—'}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-xs font-medium uppercase tracking-wide">Date NDA Signed</dt>
                    <dd>
                      {view.clientDetails.ndaSignedDate
                        ? formatDate(view.clientDetails.ndaSignedDate)
                        : '—'}
                    </dd>
                  </div>
                </>
              )}
              <div>
                <dt className="text-xs font-medium uppercase tracking-wide">Internal</dt>
                <dd>{view.clientDetails.isInternal ? 'Yes' : 'No'}</dd>
              </div>
              {!view.clientDetails.isInternal && (
                <div>
                  <dt className="text-xs font-medium uppercase tracking-wide">Invoice Frequency</dt>
                  <dd>{view.clientDetails.invoiceFrequency ?? '—'}</dd>
                </div>
              )}
            </dl>
            {view.clientDetails.billableTimeCategories.length > 0 && (
              <ul className="mt-3 flex flex-wrap gap-2 text-xs">
                {view.clientDetails.billableTimeCategories.map((category) => (
                  <li key={category} className="rounded bg-brand-taupe/30 px-2 py-0.5">
                    {category}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </Panel>
      )}

      <Panel>
        <section>
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-lg font-semibold">EDJEr Assignment History</h2>
            {/* AC-1, AC-2: the entry point for starting a new engagement, from the client's own
              record — never a standalone nav destination (feature 006, FR-055a).

              `buttonClassName` primary, not the `bg-brand-green/15` tint this shipped with — see the
              matching note on `EmployeeDetailPage` (owner request 2026-08-18). */}
            {canManageAssignments && (
              <a
                href={`/compass/client-directory/${view.id}/assignments/new`}
                className={`ml-auto ${buttonClassName({ variant: 'primary', size: 'small' })}`}
              >
                New Assignment
              </a>
            )}
          </div>

          {view.assignmentHistory.length === 0 ? (
            <p className="mt-2 text-sm">No EDJErs have been assigned to this client.</p>
          ) : (
            <>
              {/* Issue #658 — split into Current/Former. A section that has nothing to show is
                  omitted outright, heading and all, rather than rendered empty. */}
              {current.length > 0 && (
                <div className="mt-4">
                  <h3 className="text-sm font-semibold text-brand-gray uppercase tracking-wide">
                    Current Client Assignments
                  </h3>
                  <div className="mt-2">
                    <AssignmentHistoryTable
                      captionPrefix="Current"
                      rows={current}
                      clientId={view.id}
                      isInternal={view.isInternal}
                    />
                  </div>
                </div>
              )}

              {former.length > 0 && (
                <div className="mt-4">
                  <h3 className="text-sm font-semibold text-brand-gray uppercase tracking-wide">
                    Former Client Assignments
                  </h3>
                  <div className="mt-2">
                    <AssignmentHistoryTable
                      captionPrefix="Former"
                      rows={former}
                      clientId={view.id}
                      isInternal={view.isInternal}
                    />
                  </div>
                </div>
              )}
            </>
          )}
        </section>
      </Panel>
    </main>
  );
}

/**
 * One Current- or Former-section table (issue #658) — the row markup itself doesn't differ between
 * the two, only which rows and which caption.
 */
function AssignmentHistoryTable({
  captionPrefix,
  rows,
  clientId,
  isInternal,
}: {
  captionPrefix: string;
  rows: ClientAssignmentHistoryRow[];
  clientId: number;
  isInternal: boolean;
}) {
  return (
    // `StackedRows`, not a hand-rolled table: below `md` each assignment becomes a label-value card,
    // which is what removes the 20px clip that made `09/09/2026` render as `09/09/202` on an internal
    // client. It also brings this panel onto the shared primitive vocabulary (CHK009) — the
    // hand-rolled markup predates it.
    <StackedRows
      caption={`${captionPrefix} EDJEr assignment history`}
      columns={[
        'EDJEr',
        'Start Date',
        'End Date',
        // Issue #243: an internal client has no contracts, so the column has nothing to link to for
        // ANY tier — not merely no affordance for this particular viewer.
        ...(isInternal ? [] : ['SOW']),
      ]}
      rows={rows}
      rowKey={(row) => row.assignmentId}
      // `tabular-nums` on the CELL box, not on a span inside it — see StackedRows' `cellClassNames`
      // docstring for why that distinction is a gate property.
      cellClassNames={
        isInternal
          ? [undefined, 'tabular-nums', 'tabular-nums']
          : [undefined, 'tabular-nums', 'tabular-nums', undefined]
      }
      cells={(row) => [
        <>
          <a href={`/compass/team-directory/${row.employeeId}`} className={tableLinkClass}>
            {row.employeeName}
          </a>
          {row.employeeIsActive === false && (
            <span className="ml-2 inline-block align-middle">
              <StatusPill tone="neutral" label="Former" />
            </span>
          )}
        </>,
        // `tabular-nums` for the same two reasons as the Team Directory's hire date: a date column
        // should align down the page, and in an auto-layout table a value-dependent column width
        // shifts every column to its right. The seeded assignment dates are relative to `today`, so
        // without tabular figures these two columns reflow the SOW column as the calendar moves.
        formatDate(row.startDate),
        // Blank rather than "Current" (issue #658) — the section heading already says that.
        formatOptionalDate(row.endDate),
        // The cell array must match `columns` exactly — StackedRows pairs them BY INDEX, so a length
        // mismatch mislabels values rather than failing loudly.
        ...(isInternal
          ? []
          : [
              // AC-16 — the affordance appears only where the SERVER granted it. Reaches the real
              // assignment/SOWs screen (AC-1, AC-2) — this used to point at the EDJEr's own overview
              // page, a placeholder Deviation 4 (spec 006) has since closed.
              row.canViewSow === true ? (
                <a
                  href={`/compass/client-directory/${clientId}/assignments/${row.assignmentId}`}
                  className={tableLinkClass}
                >
                  View SOW
                </a>
              ) : (
                ''
              ),
            ]),
      ]}
    />
  );
}
