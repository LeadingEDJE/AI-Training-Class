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
      /** Supplied by the route container, which is the only place that knows the origin. */
      trail: BreadcrumbSegment[];
      onSubmit: (values: CreateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
      /**
       * The selectable invoice-frequency cadences (US6, #64), supplied by the route container. Optional
       * and defaulting to empty so a caller with no cadence data still renders — the override select
       * then offers only "Use the client default", which is the correct behaviour, not a broken screen.
       */
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client is an internal EDJE ("beach") client (issue #518) — hides the invoice
       * frequency override, since internal work is never invoiced.
       *
       * **On THIS arm it is unreachable in practice, and declared only so both arms read the same.**
       * Reached from an EDJEr's record, the client is not chosen until submit through `ClientPicker`,
       * whose `ClientPickerRowDto` carries no `isInternal` — so `NewEmployeeAssignmentRoute` has
       * nothing to pass. Closing that would mean widening the picker contract on the server, which is
       * out of #518's scope; see the matching note on `AssignmentForm`'s own prop.
       */
      clientIsInternal?: boolean;
    }
  | {
      viaEmployee: false;
      fixedClient: { id: number; label: string };
      trail: BreadcrumbSegment[];
      onSubmit: (values: CreateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
      /**
       * The selectable invoice-frequency cadences (US6, #64), supplied by the route container. Optional
       * and defaulting to empty so a caller with no cadence data still renders — the override select
       * then offers only "Use the client default", which is the correct behaviour, not a broken screen.
       */
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client is an internal EDJE ("beach") client (issue #518) — hides the invoice
       * frequency override, since internal work is never invoiced. Supplied on this arm: reached from
       * a client's own record the client is FIXED and already loaded, so the flag is known before the
       * form renders (`NewClientAssignmentRoute` reads it off `useClientView`).
       */
      clientIsInternal?: boolean;
    };

/**
 * The "new assignment" screen (AC-1, AC-2, mockup screen 5's layout) — reached from an EDJEr's or a
 * Client's own record, never from a standalone route (feature 006, owner direction 2026-08-14).
 *
 * One side of EDJEr/Client is FIXED by whichever record this was reached from (shown as read-only
 * text in `AssignmentForm`); the other is chosen via `ClientPicker`/`EdjerPicker` (US2, #63) — the
 * only entity-picker idiom in this codebase, a `<select>` fed by the full, unfiltered list.
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
