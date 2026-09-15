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
 * (`/compass/team-directory/$employeeId/assignments/$assignmentId`, AC-1, AC-2). Renders a
 * different assignment shape than {@link ClientAssignmentRoute} — the two are unrelated beyond
 * sharing this component's name.
 *
 * Serves two origins by the same path — the Team Directory record and the EDJEr admin form — so
 * `viaEmployee`, not the `?from=admin` search param, decides the trail.
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
      // The `key` here is a defensive habit rather than a fix for anything observed — the router
      // already remounts this component fresh on every `$assignmentId` change via `remountDeps`.
      key={assignmentId}
      assignment={data}
      isPending={isPending}
      isError={isError}
      viaEmployee
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
        if (result.kind === 'deleted') {
          try {
            await (origin === 'admin'
              ? navigate({ to: '/admin/edjers/$edjerId', params: { edjerId: employeeId } })
              : navigate({ to: '/team-directory/$employeeId', params: { employeeId } }));
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
