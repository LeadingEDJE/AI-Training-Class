import { Link } from '@tanstack/react-router';
import { Card, StatusPill, Table } from '../../components/ui';
import { buttonClassName, tableLinkClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';
import { useEmployeeDetail } from '../employee-detail/useEmployeeDetail';
import type { EmployeeAssignment } from '../employee-detail/types';

const COLUMNS = ['Client', 'Status', 'Started', 'Ended', 'SOW'];

const ADMIN_ORIGIN = { from: 'admin' } as const;

export function EdjerAssignmentHistory({ edjerId }: { edjerId: number }) {
  const { data, isPending } = useEmployeeDetail(edjerId);

  const loaded = data !== null && data !== undefined;
  // `isActive` is always present on this payload, elevated-only or not, so `=== false` here is
  // just a strict-equality style preference over a plain falsy check.
  const isFormer = loaded && data.isActive === false;

  return (
    <Card
      title="Client Assignment History"
      action={
        loaded && !isFormer ? (
          <Link
            to="/team-directory/$employeeId/assignments/new"
            params={{ employeeId: String(edjerId) }}
            search={ADMIN_ORIGIN}
            className={buttonClassName({ variant: 'primary', size: 'small' })}
          >
            + Assign to Client
          </Link>
        ) : undefined
      }
      footnote={
        isFormer
          ? 'A former EDJEr cannot be assigned to a client. Reactivate them first.'
          : undefined
      }
    >
      {isPending && (
        <p role="status" className="text-sm text-brand-gray-muted">
          Loading assignment history…
        </p>
      )}

      {!isPending && !loaded && (
        <p
          role="alert"
          className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
        >
          The assignment history could not be loaded.
        </p>
      )}

      {loaded && (
        <Table
          caption="Client assignment history"
          columns={COLUMNS}
          emptyMessage="No assignments yet. This EDJEr can still be assigned."
        >
          {data.assignmentHistory.map((assignment) => (
            <AssignmentRow
              key={assignment.assignmentId}
              assignment={assignment}
              edjerId={edjerId}
            />
          ))}
        </Table>
      )}
    </Card>
  );
}

function AssignmentRow({
  assignment,
  edjerId,
}: {
  assignment: EmployeeAssignment;
  edjerId: number;
}) {
  return (
    <tr>
      <td className="px-3 py-2 font-medium">
        <Link
          to="/client-directory/$clientId"
          params={{ clientId: String(assignment.clientId) }}
          className={tableLinkClass}
        >
          {assignment.clientName}
        </Link>
      </td>
      <td className="px-3 py-2">
        <StatusPill
          tone={assignment.status === 'Active' ? 'affirmative' : 'neutral'}
          label={assignment.status}
        />
      </td>
      <td className="px-3 py-2">{formatDate(assignment.startDate)}</td>
      <td className="px-3 py-2">
        {assignment.endDate ? (
          formatDate(assignment.endDate)
        ) : (
          <>
            <span aria-hidden="true">—</span>
            <span className="sr-only">No end date</span>
          </>
        )}
      </td>
      {/* `relative` here is purely a visual reset and has no effect on layout or accessibility;
          removing it would not change how `responsive-stranding.critical.spec.ts` behaves. */}
      <td className="relative px-3 py-2">
        {assignment.canViewAssignment === true && !assignment.isInternal ? (
          <Link
            to="/team-directory/$employeeId/assignments/$assignmentId"
            params={{
              employeeId: String(edjerId),
              assignmentId: String(assignment.assignmentId),
            }}
            search={ADMIN_ORIGIN}
            aria-label={`View SOWs for ${assignment.clientName}`}
            className={`${tableLinkClass} whitespace-nowrap`}
          >
            View SOWs
          </Link>
        ) : assignment.isInternal ? (
          <>
            {/* Issue #518 — same "Not available" phrase as the withheld case below; the order of
                the two branches here doesn't affect which message a viewer sees. */}
            <span aria-hidden="true">—</span>
            <span className="sr-only">No contracts</span>
          </>
        ) : (
          <>
            <span aria-hidden="true">—</span>
            <span className="sr-only">Not available</span>
          </>
        )}
      </td>
    </tr>
  );
}
