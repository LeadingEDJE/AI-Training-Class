import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useEmployeeDetail } from '../employee-detail/useEmployeeDetail';
import { assignmentOriginOf, employeeAssignmentTrail } from './assignment-trail';
import { NewAssignmentPage } from './NewAssignmentPage';
import { useInvoiceFrequencyOptions } from './useInvoiceFrequencyOptions';
import { useCreateAssignment } from './useAssignments';

export function NewEmployeeAssignmentRoute() {
  const { employeeId } = useParams({ from: '/team-directory/$employeeId/assignments/new' });
  const { from } = useSearch({ from: '/team-directory/$employeeId/assignments/new' });
  const navigate = useNavigate();
  const { data: detail, isPending, isError } = useEmployeeDetail(Number(employeeId));
  const createAssignment = useCreateAssignment();
  const invoiceFrequencyTypes = useInvoiceFrequencyOptions();

  if (isPending) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p role="status">Loading the EDJEr's record…</p>
      </main>
    );
  }

  if (isError || !detail) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p role="alert">That EDJEr could not be loaded.</p>
      </main>
    );
  }

  return (
    <NewAssignmentPage
      viaEmployee
      invoiceFrequencyTypes={invoiceFrequencyTypes}
      fixedEmployee={{ id: detail.id, label: `${detail.firstName} ${detail.lastName}` }}
      trail={employeeAssignmentTrail(assignmentOriginOf(from), {
        id: detail.id,
        label: `${detail.firstName} ${detail.lastName}`,
      })}
      onSubmit={async (values) => {
        const result = await createAssignment.mutateAsync(values);
        if (result.kind === 'saved') {
          await navigate({
            to: '/team-directory/$employeeId/assignments/$assignmentId',
            params: { employeeId, assignmentId: String(result.value.id) },
            search: { from },
          });
          return { kind: 'saved' };
        }
        return result;
      }}
    />
  );
}
