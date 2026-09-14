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
 * `CC-3`'s column set, with two deliberate differences recorded in spec 010's departures table.
 *
 * The mockup's fourth column is headed **"EDJEr Status"** and carries the EDJEr's stored active flag.
 * AC-24 requires *"each assignment's start/end date **and** status"* — the ASSIGNMENT's, a different
 * datum — so the criterion wins the column (Principle X tie-break rule 1) and AC-15's inactive-EDJEr
 * disclosure moves onto the name cell as a `Former` pill, which is what `ClientViewPage` already ships.
 *
 * `Started` / `Ended` rather than the mockup's `Start Date` / `End Date`, matching the shipped
 * `EdjerAssignmentHistory` (issue #223). One-word headers across both twins beats matching one mockup's
 * wording on one of them.
 *
 * The mockup leaves its action column header blank; this labels it `SOW`, as `ClientViewPage` does. An
 * unlabelled `th` is announced as nothing, and §1.3.1 wants the association.
 */
const COLUMNS = ['EDJEr', 'Status', 'Started', 'Ended', 'SOW'];

/**
 * The same set with the SOW action dropped, for an internal EDJE ("beach") client (issue #518).
 *
 * The whole COLUMN goes, not merely the cells: an internal client has no statements of work for any
 * tier, so there is nothing for the header to label and an empty action column would read as data
 * the server withheld. The identical call `ClientViewPage` makes on the identical flag.
 *
 * **The row's cell array pairs with this BY INDEX**, so the two must be built from the same decision.
 * A length mismatch does not fail — it shifts every value one column left and mislabels the lot,
 * which reads as wrong data rather than as a bug. `AssignmentRow` derives its cells from the same
 * `isInternal`, and this panel's tests assert cell count against header count for that reason.
 */
const COLUMNS_INTERNAL = ['EDJEr', 'Status', 'Started', 'Ended'];

/**
 * The client assignment history panel — AC-24, issue #224, mockup `CC-3`.
 *
 * **It reuses feature 005's read projection rather than growing a second one.** The configuration
 * payload (`CompassClient`) carries no assignments, and adding them would duplicate a query that already
 * exists. So this panel reads `/api/compass/client-directory/{id}` through the same `useClientView` hook
 * the read-only client view uses — exactly as `EdjerAssignmentHistory` reads `useEmployeeDetail`.
 *
 * **Every viewer of this panel is a Compass Super Admin.** `CompassAdminRouteGroup` puts the whole admin
 * surface behind `RolePolicy.CompassSuperAdmin`, and that tier answers yes to all three of `CompassTier`'s
 * disclosure questions — so the history arrives complete, with `employeeIsActive` present on every row and
 * `canViewSow` granted. The withheld-value branches below are still honoured rather than assumed away:
 * the panel renders what the payload says, and a payload is not a place to be clever about what "cannot"
 * happen.
 *
 * **Status is RENDERED, never computed here** (FR-009, BR-11). The server derives it from BR-7's
 * `IsCurrent` predicate against the business date; only it knows that date and the inclusive boundary,
 * which is precisely the comparison AC-42 says independent implementations get wrong.
 *
 * **Both of `CC-3`'s affordances ship**, unlike in the EDJEr twin — which recorded them as blocked on
 * feature 006 and issue #62. Both landed (`CompassAssignmentEndpoints`, and the client-scoped
 * `assignments/new` and `assignments/$assignmentId` routes), so the controls have real destinations.
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
          // A navigation, so an anchor — wearing the mockup's primary-small button appearance through
          // the shared class helper rather than a copied class run. `Link to` keeps it inside the SPA;
          // a plain `<a href>` would full-page reload.
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

      {/* `null` is the hook's answer for a 404 OR a client this viewer is not entitled to see — the
          server deliberately does not distinguish them. Either way this is a failure to load, and
          reporting it as "no EDJErs assigned" would state something false about the client. */}
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
  /** Issue #518 — drops the SOW cell, in step with `COLUMNS_INTERNAL`. See its docstring. */
  clientIsInternal: boolean;
}) {
  return (
    <tr>
      <td className="px-3 py-2 font-medium">
        {/* EVERY name links, including a departed EDJEr's — a departure from `CC-3`, which renders its
            inactive row's name as plain text. Their record still exists and is exactly what a Super
            Admin reading history wants to open; making the row that most needs explaining the one row
            you cannot follow is the wrong trade. `ClientViewPage` already links them all.

            The mockup's `a.link` colour is `--le-blue` (#5C95FF), which measures 2.92:1 on white and
            fails AA for normal text — so tie-break rule 2 applies and this wears `tableLinkClass`. */}
        <Link
          to="/team-directory/$employeeId"
          params={{ employeeId: String(row.employeeId) }}
          className={tableLinkClass}
        >
          {row.employeeName}
        </Link>

        {/* AC-15's disclosure, as a pill on the name rather than a column (Departure 1). ABSENT means
            the server withheld it, and every row such a viewer receives is active — so absent renders
            nothing at all. `=== false` rather than `!row.employeeIsActive`, which would also fire on
            undefined and invent a claim the server never made. */}
        {row.employeeIsActive === false && (
          <span className="ml-2 inline-block align-middle">
            <StatusPill tone="neutral" label="Former" />
          </span>
        )}
      </td>

      <td className="px-3 py-2">
        {/* The word the server sent (AC-NFR-5). Never recomputed from the dates beside it. */}
        <StatusPill tone={row.status === 'Active' ? 'affirmative' : 'neutral'} label={row.status} />
      </td>

      {/* `tabular-nums` for the same reason the Team Directory's hire date has it: a date column should
          align down the page, and in an auto-layout table a value-dependent column width shifts every
          column to its right. */}
      <td className="px-3 py-2 tabular-nums">{formatDate(row.startDate)}</td>
      <td className="px-3 py-2 tabular-nums">
        {row.endDate ? (
          formatDate(row.endDate)
        ) : (
          <>
            {/* An em dash rather than "Current", and rather than a blank. A blank reads as missing
                data. "Current" would restate, in a date column, a judgement the Status column already
                published — and the two can visibly disagree: the server can report Inactive on a row
                with no end date, which would then read "Inactive … Current". This panel's own test
                pins that case. The date column states the fact; Status states the derivation. */}
            <span aria-hidden="true">—</span>
            <span className="sr-only">No end date</span>
          </>
        )}
      </td>

      {/* Issue #518 — the cell goes with its header, or the two fall out of step and every value
          shifts a column. `canViewSow` is a PERMISSION the server grants; this is whether the
          destination holds anything at all, so an internal client's row offers nothing even where
          that permission WAS granted. */}
      {!clientIsInternal && (
        <td className="px-3 py-2">
          {/* AC-16 — the affordance appears only where the SERVER granted it. Absent means not
              granted, so it cannot be rendered from a client-side guess. */}
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
