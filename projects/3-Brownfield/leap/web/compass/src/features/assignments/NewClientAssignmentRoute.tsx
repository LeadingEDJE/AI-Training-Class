import { useNavigate, useParams } from '@tanstack/react-router';
import { useClientView } from '../client-view/useClientView';
import { clientAssignmentTrail } from './assignment-trail';
import { NewAssignmentPage } from './NewAssignmentPage';
import { useInvoiceFrequencyOptions } from './useInvoiceFrequencyOptions';
import { useCreateAssignment } from './useAssignments';

/**
 * The "new assignment" screen reached from a Client's own record
 * (`/compass/client-directory/$clientId/assignments/new`, AC-1, AC-2). Reuses `useClientView`
 * purely for the client's display name — no new endpoint needed for that.
 */
export function NewClientAssignmentRoute() {
  const { clientId } = useParams({ from: '/client-directory/$clientId/assignments/new' });
  const navigate = useNavigate();
  const { data: view, isPending, isError } = useClientView(Number(clientId));
  const createAssignment = useCreateAssignment();
  const invoiceFrequencyTypes = useInvoiceFrequencyOptions();

  if (isPending) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p role="status">Loading the client…</p>
      </main>
    );
  }

  if (isError || !view) {
    return (
      <main className="mx-auto max-w-4xl px-6 py-8 text-brand-gray">
        <p role="alert">That client could not be loaded.</p>
      </main>
    );
  }

  return (
    <NewAssignmentPage
      viaEmployee={false}
      invoiceFrequencyTypes={invoiceFrequencyTypes}
      // Issue #518 — the client is fixed and already loaded here, so its internal-EDJE flag is known
      // before the form renders. `useClientView`'s view carries it for every tier (issue #243).
      clientIsInternal={view.isInternal}
      fixedClient={{ id: view.id, label: view.clientName }}
      trail={clientAssignmentTrail({ id: view.id, label: view.clientName })}
      onSubmit={async (values) => {
        const result = await createAssignment.mutateAsync(values);
        if (result.kind === 'saved') {
          await navigate({
            to: '/client-directory/$clientId/assignments/$assignmentId',
            params: { clientId, assignmentId: String(result.value.id) },
          });
          return { kind: 'saved' };
        }
        return result;
      }}
    />
  );
}
