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
      // The router sets `remountDeps` on `$assignmentId` in `routes/router.ts`, so this key is
      // redundant with that config and only guards against a future removal of it.
      key={assignmentId}
      assignment={data}
      isPending={isPending}
      isError={isError}
      viaEmployee={false}
      trail={data ? clientAssignmentTrail({ id: data.clientId, label: data.clientName }) : []}
      invoiceFrequencyTypes={invoiceFrequencyTypes}
      canManageAssignments={canManageAssignments}
      onSubmit={(values) =>
        updateAssignment.mutateAsync({ id: Number(assignmentId), request: values })
      }
      canDeleteAssignment={canDeleteAssignment}
      onDeleteAssignment={async () => {
        const result = await deleteAssignment.mutateAsync(Number(assignmentId));
        // A failed navigation here is reported back to the page as a failed delete, since the two
        // outcomes share the same banner (PR #604 review).
        if (result.kind === 'deleted') {
          try {
            await navigate({ to: '/client-directory/$clientId', params: { clientId } });
          } catch (navigationError) {
            console.error(
              'Compass: navigation after deleting an assignment failed',
              navigationError,
            );
          }
        }
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
