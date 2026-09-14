import { Link } from '@tanstack/react-router';
import { Card, StatusPill, Table } from '../../components/ui';
import { buttonClassName, tableLinkClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';
import { useEmployeeDetail } from '../employee-detail/useEmployeeDetail';
import type { EmployeeAssignment } from '../employee-detail/types';

// Named rather than left blank: an unlabelled header is announced as nothing.
const COLUMNS = ['Client', 'Status', 'Started', 'Ended', 'SOW'];

/** Declared once because both links must agree. Validated by the routes that read it. */
const ADMIN_ORIGIN = { from: 'admin' } as const;

/**
 * The EDJEr assignment history panel — AC-20, issue #223, mockup `EC-3`.
 *
 * Reuses feature 005's read projection (`useEmployeeDetail`) rather than growing a second one: the
 * configuration payload carries no assignments.
 *
 * Both affordances navigate to the employee-nested assignment screens, carrying `?from=admin` so those
 * screens breadcrumb back to this form. An exact mirror of `ClientAssignmentHistory`.
 *
 * No role check here: every route rendering this form requires `RolePolicy.CompassSuperAdmin`, and
 * `EdjerFormPage` renders a refusal instead of the form when the record read is refused. The per-row SOW
 * affordance is still gated on the server-granted `canViewAssignment`.
 *
 * Status is RENDERED, never computed here (FR-029, BR-11) — the server derives it from the business
 * date against BR-7's `IsCurrent` predicate.
 */
export function EdjerAssignmentHistory({ edjerId }: { edjerId: number }) {
  const { data, isPending } = useEmployeeDetail(edjerId);

  const loaded = data !== null && data !== undefined;
  // `=== false`, not falsy: `isActive` is elevated-only and omitted rather than sent false.
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

      {/* `null` covers a 404 AND a record this viewer may not see — the server does not distinguish
          them (FR-021). Reporting either as "no assignments yet" would state something false. */}
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
        {/* AC-8: a client in an assignment history links to that client's view. */}
        <Link
          to="/client-directory/$clientId"
          params={{ clientId: String(assignment.clientId) }}
          className={tableLinkClass}
        >
          {assignment.clientName}
        </Link>
      </td>
      <td className="px-3 py-2">
        {/* The word the server sent (AC-NFR-5). Never recomputed from the dates beside it. */}
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
            {/* An em dash, not EmployeeDetailPage's "Current": that word would restate the Status
                column beside it, and the two can disagree (a row reported Inactive with no end date). */}
            <span aria-hidden="true">—</span>
            <span className="sr-only">No end date</span>
          </>
        )}
      </td>
      {/* `relative`, and it is load-bearing rather than cosmetic (issue #518).

          This is the LAST column, so at 390px it sits beyond the visible edge inside the table's
          own `overflow-x:auto` region. Both non-link branches below carry an `sr-only` span, and
          Tailwind's `sr-only` is `position:absolute` -- with no positioned ancestor its containing
          block is the INITIAL one (the document), and `overflow` clips an absolutely-positioned
          descendant only when the clipping element IS its containing block. So the span escapes the
          region and extends `document.documentElement.scrollWidth` to wherever the last column
          starts, which makes the PAGE scroll horizontally: Gate E in
          `responsive-stranding.critical.spec.ts`, measured at doc 457 against a 390 body.

          Making the CELL the containing block confines it. The earlier columns were never at risk
          because their static positions fall inside the visible width -- which is why the
          long-standing `sr-only` in `Ended` above is fine and this one was not. Do not remove
          `relative` while either branch below renders an `sr-only`.

          Found by the stranding gate on the first push that made an internal row take a non-link
          branch; the withheld branch had the same latent defect and had simply never rendered here. */}
      <td className="relative px-3 py-2">
        {/* AC-20's "View SOWs" delta, and AC-16/FR-025's gate on it.

            A LINK, to the assignment screen where SOWs are managed. #448 briefly made this a button
            opening a dialog; the owner asked for the navigation back (2026-08-28), so that reaching an
            assignment has one shape wherever it starts from. That screen is also the only place an
            assignment can be END-DATED, which is the recovery path when AC-19 blocks a deactivation.

            Gated on a SERVER-GRANTED entitlement, not on a client-side role check: `canViewAssignment`
            is omitted rather than sent false for a viewer who may not open the assignment, so an
            affordance the server withheld cannot be rendered from this payload at all. That every
            route serving this form already requires the Compass root policy is not the reason to skip
            the check — the payload is the authority on what this row may offer, and reading it is
            cheaper than reasoning about which roles reach the screen.

            The accessible name EXTENDS the visible "View SOWs" with the client, which is what keeps a
            column of identical links distinguishable while staying WCAG 2.5.3-safe. Replacing the
            label — making the client name itself the SOW link — is the defect `EdjerListPage` records
            declining to copy. */}
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
            {/* Issue #518 — a DIFFERENT statement from the withheld case below, and deliberately
                worded apart from it. "Not available" says the server declined to grant this viewer
                the read. This says the section exists and an internal client has nothing in it:
                internal work is EDJE on EDJE, so there is no contract to sign. Reusing one phrase
                for both would tell a screen-reader user the wrong reason, and the two are fixed by
                different actions — one by permissions, one not at all.

                Checked BEFORE the withheld branch so the reason given is the one that actually
                applies: a row can be both internal and ungranted, and "no contracts" is the more
                specific truth. */}
            <span aria-hidden="true">—</span>
            <span className="sr-only">No contracts</span>
          </>
        ) : (
          <>
            {/* Withheld, not empty — said so rather than leaving a cell that reads as missing data. */}
            <span aria-hidden="true">—</span>
            <span className="sr-only">Not available</span>
          </>
        )}
      </td>
    </tr>
  );
}
