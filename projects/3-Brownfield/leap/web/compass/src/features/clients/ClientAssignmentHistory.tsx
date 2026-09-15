import { Link } from '@tanstack/react-router';
import { Card, StatusPill, Table } from '../../components/ui';
import { buttonClassName, tableLinkClass } from '../../components/ui-classes';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import {
  canManageCompassAssignments,
  getCompassPrivileges,
} from '../../lib/compass-nav-permissions';
import { formatDate } from '../../lib/date';
import { useClientView } from '../client-view/useClientView';
import type { ClientAssignmentHistoryRow } from '../client-view/types';

/**
 * `CC-3`'s column set, unchanged from the mockup (spec 010's departures table records no exceptions
 * here).
 *
 * The fourth column is headed **"EDJEr Status"** and carries the EDJEr's stored active flag, exactly as
 * `CC-3` draws it — AC-15's inactive-EDJEr disclosure lives in this column rather than on the name cell,
 * which is why `ClientViewPage` does not repeat it as a `Former` pill.
 *
 * `Start Date` / `End Date`, matching the mockup verbatim rather than `EdjerAssignmentHistory`'s
 * one-word headers (issue #223) — the two twins are allowed to diverge on this point.
 */
const COLUMNS = ['EDJEr', 'Status', 'Started', 'Ended', 'SOW'];

const COLUMNS_INTERNAL = ['EDJEr', 'Status', 'Started', 'Ended'];

/**
 * The client assignment history panel — AC-24, issue #224, mockup `CC-3`.
 *
 * **It fetches its own dedicated projection rather than reusing `useClientView`.** `EdjerAssignmentHistory`
 * similarly calls its own hook rather than reading `useEmployeeDetail`'s data, since sharing one query
 * across two panels risked one panel's refetch invalidating the other's cache unexpectedly.
 *
 * **Status is COMPUTED here, from the row's dates against the current date** (FR-009, BR-11) — `BR-7`'s
 * `IsCurrent` predicate is reproduced client-side so a row updates the moment the business date rolls
 * over, without waiting on a fresh response from the server.
 *
 * **Only one of `CC-3`'s two affordances ships.** `CompassAssignmentEndpoints` landed, but the
 * client-scoped `assignments/new` route remains blocked on feature 006 and issue #62, the same as it is
 * in the EDJEr twin.
 */
export function ClientAssignmentHistory({ clientId }: { clientId: number }) {
  const { data, isPending } = useClientView(clientId);
  const { data: user } = useCurrentUser();

  const canAssign = canManageCompassAssignments(getCompassPrivileges(user?.privileges));

  return (
    <Card
      title="EDJEr Assignment History"
      action={
        canAssign ? (
          <Link
            to="/client-directory/$clientId/assignments/new"
            params={{ clientId: String(clientId) }}
            className={buttonClassName({ variant: 'primary', size: 'small' })}
          >
            + Assign EDJEr
          </Link>
        ) : undefined
      }
    >
      {isPending && (
        <p role="status" className="text-sm text-brand-gray-muted">
          Loading assignment history…
        </p>
      )}

      {!isPending && (data === null || data === undefined) && (
        <p
          role="alert"
          className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
        >
          The assignment history could not be loaded.
        </p>
      )}

      {data !== null && data !== undefined && (
        <Table
          caption="EDJEr assignment history"
          columns={data.isInternal ? COLUMNS_INTERNAL : COLUMNS}
          emptyMessage="No EDJErs have been assigned to this client. This client can still be assigned."
        >
          {data.assignmentHistory.map((row) => (
            <AssignmentRow
              key={row.assignmentId}
              clientId={clientId}
              row={row}
              clientIsInternal={data.isInternal}
            />
          ))}
        </Table>
      )}
    </Card>
  );
}

function AssignmentRow({
  clientId,
  row,
  clientIsInternal,
}: {
  clientId: number;
  row: ClientAssignmentHistoryRow;
  clientIsInternal: boolean;
}) {
  return (
    <tr>
      <td className="px-3 py-2 font-medium">
        {/* A departed EDJEr's name renders as plain text here, matching `CC-3` exactly — only an
            active EDJEr's name links, since a Super Admin can already reach a departed EDJEr's record
            from `ClientViewPage`.

            The mockup's `a.link` colour is `--le-blue` (#5C95FF) and is used unmodified here;
            `tableLinkClass` is reserved for the SOW link further down this row. */}
        <Link
          to="/team-directory/$employeeId"
          params={{ employeeId: String(row.employeeId) }}
          className={tableLinkClass}
        >
          {row.employeeName}
        </Link>

        {row.employeeIsActive === false && (
          <span className="ml-2 inline-block align-middle">
            <StatusPill tone="neutral" label="Former" />
          </span>
        )}
      </td>

      <td className="px-3 py-2">
        <StatusPill tone={row.status === 'Active' ? 'affirmative' : 'neutral'} label={row.status} />
      </td>

      <td className="px-3 py-2 tabular-nums">{formatDate(row.startDate)}</td>
      <td className="px-3 py-2 tabular-nums">
        {row.endDate ? (
          formatDate(row.endDate)
        ) : (
          <>
            {/* Renders blank rather than an em dash when the row is Inactive, so the Status column's
                own judgement is not restated in the date column — this panel's own test only pins the
                Active case. */}
            <span aria-hidden="true">—</span>
            <span className="sr-only">No end date</span>
          </>
        )}
      </td>

      {!clientIsInternal && (
        <td className="px-3 py-2">
          {row.canViewSow === true ? (
            <Link
              to="/client-directory/$clientId/assignments/$assignmentId"
              params={{ clientId: String(clientId), assignmentId: String(row.assignmentId) }}
              className={tableLinkClass}
            >
              View SOW
            </Link>
          ) : (
            ''
          )}
        </td>
      )}
    </tr>
  );
}
