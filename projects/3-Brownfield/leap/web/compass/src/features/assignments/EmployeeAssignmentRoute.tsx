import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import {
  canDeleteCompassAssignments,
  canManageCompassAssignments,
  getCompassPrivileges,
} from '../../lib/compass-nav-permissions';
import { AssignmentDetailPage } from './AssignmentDetailPage';
import { assignmentOriginOf, employeeAssignmentTrail } from './assignment-trail';
import { useInvoiceFrequencyOptions } from './useInvoiceFrequencyOptions';
import {
  useAssignment,
  useCreateSow,
  useDeleteAssignment,
  useDeleteSow,
  useSows,
  useUpdateAssignment,
  useUpdateSow,
} from './useAssignments';

/**
 * The assignment detail screen reached from an EDJEr's own record
 * (`/compass/team-directory/$employeeId/assignments/$assignmentId`, AC-1, AC-2). Renders the exact
 * same underlying assignment as {@link ClientAssignmentRoute} — only the breadcrumb differs. Reads its
 * own route parameter rather than taking it as a prop, matching `EmployeeDetailRoute`/`ClientViewRoute`.
 *
 * Serves two origins by the same path — the Team Directory record and the EDJEr admin form — so
 * `?from=admin`, not `viaEmployee`, decides the trail.
 */
export function EmployeeAssignmentRoute() {
  const { employeeId, assignmentId } = useParams({
    from: '/team-directory/$employeeId/assignments/$assignmentId',
  });
  const { from } = useSearch({ from: '/team-directory/$employeeId/assignments/$assignmentId' });
  const navigate = useNavigate();
  const origin = assignmentOriginOf(from);
  const { data, isPending, isError } = useAssignment(Number(assignmentId));
  const updateAssignment = useUpdateAssignment();
  const deleteAssignment = useDeleteAssignment();
  const { data: user } = useCurrentUser();
  const canManageAssignments = canManageCompassAssignments(getCompassPrivileges(user?.privileges));
  const canDeleteAssignment = canDeleteCompassAssignments(getCompassPrivileges(user?.privileges));
  const {
    data: sows,
    isPending: sowsIsPending,
    isError: sowsIsError,
  } = useSows(Number(assignmentId), canManageAssignments);
  const createSow = useCreateSow(Number(assignmentId));
  const updateSow = useUpdateSow(Number(assignmentId));
  const deleteSow = useDeleteSow(Number(assignmentId));
  const invoiceFrequencyTypes = useInvoiceFrequencyOptions();

  return (
    <AssignmentDetailPage
      // The router reuses this component across `$assignmentId` changes (no `remountDeps`, and no
      // default set in `routes/router.ts`), so without a key the previous assignment's local state
      // — a delete-refusal banner, an open SOW modal — outlives its own subject (PR #604 review).
      key={assignmentId}
      assignment={data}
      isPending={isPending}
      isError={isError}
      viaEmployee
      // Empty until loaded; the page renders its status/error state before reaching the breadcrumb.
      trail={
        data
          ? employeeAssignmentTrail(origin, {
              id: data.employeeId,
              label: data.employeeName,
            })
          : []
      }
      invoiceFrequencyTypes={invoiceFrequencyTypes}
      canManageAssignments={canManageAssignments}
      onSubmit={(values) =>
        updateAssignment.mutateAsync({ id: Number(assignmentId), request: values })
      }
      canDeleteAssignment={canDeleteAssignment}
      onDeleteAssignment={async () => {
        const result = await deleteAssignment.mutateAsync(Number(assignmentId));
        // This screen's own subject is gone once the delete succeeds — return to whichever
        // parent screen the trail above points at, matching the origin this route was reached
        // through (`employeeAssignmentTrail`'s own two shapes).
        if (result.kind === 'deleted') {
          try {
            await (origin === 'admin'
              ? navigate({ to: '/admin/edjers/$edjerId', params: { edjerId: employeeId } })
              : navigate({ to: '/team-directory/$employeeId', params: { employeeId } }));
          } catch (navigationError) {
            // The delete already committed and cannot be undone, so a navigation failure must NOT
            // be reported as a failed delete (PR #604 review).
            console.error(
              'Compass: navigation after deleting an assignment failed',
              navigationError,
            );
          }
        }
        // Handed back so the page can surface a rejection: navigating is the only visible effect
        // this container has, so a dropped outcome is a silent failure (PR #604 review).
        return result;
      }}
      sows={sows}
      sowsIsPending={sowsIsPending}
      sowsIsError={sowsIsError}
      onCreateSow={(values) => createSow.mutateAsync(values)}
      onUpdateSow={(sowId, values) => updateSow.mutateAsync({ sowId, request: values })}
      onDeleteSow={(sowId) => deleteSow.mutateAsync(sowId)}
    />
  );
}
