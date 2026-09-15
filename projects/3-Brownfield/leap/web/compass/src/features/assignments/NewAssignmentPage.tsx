import { Card, PageHeader } from '../../components/ui';
import { AssignmentBreadcrumb } from './AssignmentBreadcrumb';
import type { BreadcrumbSegment } from './assignment-trail';
import {
  AssignmentForm,
  type AssignmentFormOutcome,
  type CreateAssignmentFormValues,
  type InvoiceFrequencyOption,
} from './AssignmentForm';
import { ClientPicker } from './ClientPicker';
import { EdjerPicker } from './EdjerPicker';

type NewAssignmentPageProps =
  | {
      viaEmployee: true;
      fixedEmployee: { id: number; label: string };
      trail: BreadcrumbSegment[];
      onSubmit: (values: CreateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client is an internal EDJE ("beach") client — hides the invoice frequency
       * override, since internal work is never invoiced.
       *
       * Populated on this arm the same way as the other: `ClientPicker`'s row already carries
       * `isInternal`, so `NewEmployeeAssignmentRoute` passes it through before the client is even
       * chosen, matching `AssignmentForm`'s own prop.
       */
      clientIsInternal?: boolean;
    }
  | {
      viaEmployee: false;
      fixedClient: { id: number; label: string };
      trail: BreadcrumbSegment[];
      onSubmit: (values: CreateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      clientIsInternal?: boolean;
    };

/**
 * The "new assignment" screen — reachable as a standalone route as well as from an EDJEr's or
 * Client's own record.
 *
 * Both EDJEr and Client are chosen via `ClientPicker`/`EdjerPicker`; neither side is fixed by the
 * record this screen was reached from.
 */
export function NewAssignmentPage(props: NewAssignmentPageProps) {
  const breadcrumb = <AssignmentBreadcrumb trail={props.trail} current="New Assignment" />;

  return (
    <main className="mx-auto flex max-w-4xl flex-col gap-6 px-6 py-8 text-brand-gray">
      <PageHeader title="New Assignment" breadcrumb={breadcrumb} />

      <Card title="Assignment Details">
        {props.viaEmployee ? (
          <AssignmentForm
            mode="create"
            fixedEmployee={props.fixedEmployee}
            invoiceFrequencyTypes={props.invoiceFrequencyTypes}
            clientIsInternal={props.clientIsInternal}
            renderClientPicker={(value, onChange, fieldId) => (
              <ClientPicker id={fieldId} value={value} onChange={onChange} />
            )}
            onSubmit={props.onSubmit}
          />
        ) : (
          <AssignmentForm
            mode="create"
            fixedClient={props.fixedClient}
            invoiceFrequencyTypes={props.invoiceFrequencyTypes}
            clientIsInternal={props.clientIsInternal}
            renderEmployeePicker={(value, onChange, fieldId) => (
              <EdjerPicker id={fieldId} value={value} onChange={onChange} />
            )}
            onSubmit={props.onSubmit}
          />
        )}
      </Card>
    </main>
  );
}
