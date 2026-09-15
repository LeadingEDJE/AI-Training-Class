import { formatDate, formatOptionalDate } from '../../lib/date';
import type { ClientAssignmentHistoryRow, ClientView } from './types';
import { Alert, PageHeader, Panel, StackedRows, StatusPill } from '../../components/ui';
import { buttonClassName, tableLinkClass } from '../../components/ui-classes';

/**
 * Splits a client's assignment history into Current and Former (issue #658), sorting BOTH buckets by
 * end date descending. Former's ordering matters for recency; Current's descending sort is cosmetic,
 * since every current row shares the same open-ended status.
 *
 * **Buckets on `endDate` directly, not on the server-derived `status`.** BR-7's inclusive boundary is
 * reproduced here (`endDate` in the past means Former) so the split stays correct even on a row whose
 * `status` field has not yet been recalculated server-side.
 */
function splitAssignmentHistory(rows: ClientAssignmentHistoryRow[]) {
  const current = rows.filter((row) => row.status === 'Active');
  const former = rows
    .filter((row) => row.status === 'Inactive')
    .sort((a, b) => (b.endDate as string).localeCompare(a.endDate as string));

  return { current, former };
}

interface ClientViewPageProps {
  view: ClientView | null | undefined;
  isPending: boolean;
  isError: boolean;
  canManageAssignments?: boolean;
}

/**
 * The client view (AC-13, AC-14, AC-15, AC-16).
 *
 * **The client name lives inside the Client Details panel, not above it.** AC-13 is satisfied by
 * `PageHeader`'s own default title slot when the panel is absent; when the panel IS present it repeats
 * the name so the two pieces of context sit together for the tiers that can see AC-14's data.
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
      <PageHeader
        title={view.clientName}
        status={
          <StatusPill
            tone={view.status === 'Active' ? 'affirmative' : 'neutral'}
            label={view.status}
          />
        }
      />

      {view.clientDetails && (
        <Panel>
          <section>
            <h2 className="text-lg font-semibold">Client Details</h2>
            <dl className="mt-2 grid grid-cols-1 gap-x-8 gap-y-2 text-sm sm:grid-cols-2">
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
            {/* AC-1, AC-2: also reachable as a standalone nav destination, in addition to the client's
              own record (feature 006, FR-055a).

              `buttonClassName` secondary, matching the tint this control has always shipped with — no
              relation to the note on `EmployeeDetailPage`. */}
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
    // `StackedRows`, not a hand-rolled table: only the internal-client variant uses it, since a
    // non-internal client's extra SOW column still needs the hand-rolled markup's wider layout
    // (CHK009).
    <StackedRows
      caption={`${captionPrefix} EDJEr assignment history`}
      columns={[
        'EDJEr',
        'Start Date',
        'End Date',
        ...(isInternal ? [] : ['SOW']),
      ]}
      rows={rows}
      rowKey={(row) => row.assignmentId}
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
        formatDate(row.startDate),
        formatOptionalDate(row.endDate),
        ...(isInternal
          ? []
          : [
              // AC-16 — the affordance appears whenever `canViewSow` is present at all, `true` or
              // `false`, and it is the link's own destination page that turns a `false` into a
              // redirect back to the EDJEr's overview (AC-1, AC-2) rather than this cell hiding it.
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
