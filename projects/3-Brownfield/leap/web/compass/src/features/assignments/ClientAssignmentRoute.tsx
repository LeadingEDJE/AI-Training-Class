import { useNavigate, useParams } from '@tanstack/react-router';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import {
  canDeleteCompassAssignments,
  canManageCompassAssignments,
  getCompassPrivileges,
} from '../../lib/compass-nav-permissions';
import { AssignmentDetailPage } from './AssignmentDetailPage';
import { clientAssignmentTrail } from './assignment-trail';
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
 * The assignment detail screen reached from a Client's own record
 * (`/compass/client-directory/$clientId/assignments/$assignmentId`, AC-1, AC-2). Renders the exact
 * same underlying assignment as {@link EmployeeAssignmentRoute} — only the breadcrumb differs, which
 * `AssignmentDetailPage`'s `viaEmployee` flag controls. Reads its own route parameter rather than
 * taking it as a prop, matching `EmployeeDetailRoute`/`ClientViewRoute`.
 */
export function ClientAssignmentRoute() {
  const { assignmentId, clientId } = useParams({
    from: '/client-directory/$clientId/assignments/$assignmentId',
  });
  const navigate = useNavigate();
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
      viaEmployee={false}
      // Empty until loaded; the page renders its status/error state before reaching the breadcrumb.
      trail={data ? clientAssignmentTrail({ id: data.clientId, label: data.clientName }) : []}
      invoiceFrequencyTypes={invoiceFrequencyTypes}
      canManageAssignments={canManageAssignments}
      onSubmit={(values) =>
        updateAssignment.mutateAsync({ id: Number(assignmentId), request: values })
      }
      canDeleteAssignment={canDeleteAssignment}
      onDeleteAssignment={async () => {
        const result = await deleteAssignment.mutateAsync(Number(assignmentId));
        // This screen's own subject is gone once the delete succeeds — return to the client
        // record it was reached from, matching the trail this screen renders.
        if (result.kind === 'deleted') {
          try {
            await navigate({ to: '/client-directory/$clientId', params: { clientId } });
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
