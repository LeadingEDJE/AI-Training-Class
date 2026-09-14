import { useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { Button, FormField, FormGrid } from '../../components/ui';
import { fieldControlClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';

/** The values a create submits — contract §2 `CreateAssignmentRequest`. */
/**
 * One selectable invoice-frequency cadence (feature 006 US6, issue #64). Supplied by the route
 * container via {@link useInvoiceFrequencyOptions} rather than fetched here — this component takes
 * every input as a prop and its suite renders it with no `QueryClientProvider`.
 */
export interface InvoiceFrequencyOption {
  id: number;
  typeName: string;
}

export interface CreateAssignmentFormValues {
  employeeId: number;
  clientId: number;
  startDate: string;
  endDate: string | null;
  note: string | null;
  /** The invoice-frequency override, or `null` to bill the way the client does (US6, #64). */
  invoiceFrequencyTypeId: number | null;
}

/** The values an edit submits — contract §2 `UpdateAssignmentRequest`. No `employeeId`/`clientId`. */
export interface UpdateAssignmentFormValues {
  startDate: string;
  endDate: string | null;
  note: string | null;
  invoiceFrequencyTypeId: number | null;
}

/** The outcome of a submit, mirroring the lookup write shapes elsewhere in `web/compass`. */
export type AssignmentFormOutcome = { kind: 'saved' } | { kind: 'rejected'; message: string };

/** The existing values an edit pre-populates from. */
export interface AssignmentFormInitialValues {
  employeeId: number;
  clientId: number;
  startDate: string;
  endDate: string | null;
  note: string | null;
  invoiceFrequencyTypeId: number | null;
}

type AssignmentFormProps =
  | {
      mode: 'create';
      /** Present when the EDJEr side is fixed by the route (e.g. reached from an EDJEr's own record). */
      fixedEmployee?: { id: number; label: string };
      /** Present when the client side is fixed by the route (e.g. reached from a client's own record). */
      fixedClient?: { id: number; label: string };
      /**
       * Renders the picker for whichever side is NOT fixed. Receives the current value, a setter, and
       * the id `FormField` generated for this row.
       *
       * **Forward that id onto the rendered `<select>`.** `FormField` puts it on its `<label
       * htmlFor>`, so a picker that invents its own id instead renders a control with NO accessible
       * name — invisible to `getByRole('combobox', { name: ... })` and to a screen reader alike. That
       * was the case until US6 (#64) added a third select to this form and the previously-unambiguous
       * bare `getByRole('combobox')` queries started matching more than one element.
       */
      renderEmployeePicker?: (
        value: string,
        onChange: (value: string) => void,
        fieldId: string,
      ) => React.ReactElement;
      renderClientPicker?: (
        value: string,
        onChange: (value: string) => void,
        fieldId: string,
      ) => React.ReactElement;
      /**
       * The invoice-frequency types offered as an override (US6, #64). ACTIVE ones only — the server
       * refuses a retired id regardless, which is what makes this list a convenience and never the
       * control (FR-038, FR-041). Optional: omitted renders the no-override option alone.
       */
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client this assignment is for is an internal EDJE ("beach") client (issue #518).
       * `true` omits the invoice-frequency override entirely — internal work is EDJE on EDJE and is
       * never invoiced, so a cadence for it is meaningless rather than merely unset.
       *
       * **Known limitation, deliberately not closed here.** On the new-assignment screen reached from
       * an EDJEr's record the client is not chosen until submit, through `ClientPicker`, and
       * `ClientPickerRowDto` does not carry `isInternal`. This component takes every input as a prop
       * and fetches nothing — which is what lets its suite render with no `QueryClientProvider` — so
       * it cannot discover the flag itself. That path therefore still shows the select. Closing it
       * means widening the picker contract, which is a server change out of #518's scope; the client
       * record's own "New Assignment" entry point (the one the issue's screenshot came from) does
       * thread the flag.
       */
      clientIsInternal?: boolean;
      onSubmit: (values: CreateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
    }
  | {
      mode: 'edit';
      initialValues: AssignmentFormInitialValues;
      /**
       * Whether this viewer may actually save a change — Compass Ops or Super Admin. Compass Admin
       * and Sales can reach this screen too (AC-16/FR-025), but only to view: `false` renders the
       * dates and note as plain text, with no Save button, rather than a form that will only ever
       * be refused. The server enforces the write independently regardless (FR-006).
       */
      canEdit: boolean;
      /**
       * The invoice-frequency types offered as an override (US6, #64). ACTIVE ones only — the server
       * refuses a retired id regardless, which is what makes this list a convenience and never the
       * control (FR-038, FR-041). Optional: omitted renders the no-override option alone.
       */
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client this assignment is for is an internal EDJE ("beach") client (issue #518).
       * `true` omits the invoice-frequency row from both the editable form and the read-only view —
       * internal work is never invoiced. See the create arm's copy of this prop for the one path that
       * cannot supply it.
       *
       * **Hiding the control does NOT clear the value.** `invoiceFrequencyTypeId` stays in the form's
       * state and still travels on submit, so saving an unrelated edit cannot silently null a stored
       * override; `AssignmentForm.test.tsx` pins that directly.
       */
      clientIsInternal?: boolean;
      onSubmit: (values: UpdateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
    };

/**
 * US1/#62 — create and edit an assignment, per mockup screen 5's "Assignment Details" card layout
 * (`PageHeader`/`Card`/`FormGrid` from `ui.tsx`, not the raw markup this form used before feature
 * 006's entry-point pivot).
 *
 * **Renders NO audit-reason input** (FR-050): reasons are system-generated, and
 * `CompassAuditReason`'s own documentation warns against adding one to any configuration form.
 *
 * **`employeeId`/`clientId` are absent in edit mode** — moving an assignment to a different EDJEr
 * or client is not a criterion (contract §2).
 *
 * **In create mode, one side of EDJEr/Client is FIXED by the route the form was reached from**
 * (the EDJEr's own record fixes EDJEr; the client's own record fixes Client — AC-1, AC-2), rendered
 * as read-only text, never a picker for the side already known. The other side renders whatever
 * `renderEmployeePicker`/`renderClientPicker` supplies (`ClientPicker.tsx`/`EdjerPicker.tsx`); with
 * neither prop given, a plain numeric input remains as a placeholder-of-last-resort.
 */
export function AssignmentForm(props: AssignmentFormProps) {
  const [error, setError] = useState<string | null>(null);

  // INJECTED, not fetched. This component has no data dependencies of its own — its EDJEr and client
  // pickers are passed in too — which is what lets its tests render it without a QueryClientProvider
  // and keeps "which cadences are offered" a decision of the screen that knows the context. Defaults
  // to empty so a caller that has not loaded them yet renders the no-override option alone rather
  // than crashing.
  const activeCadences = props.invoiceFrequencyTypes ?? [];

  // Issue #518 — an internal client is never invoiced, so the override has nothing to express. The
  // FIELD stays in form state either way (see the prop's docstring): this decides what RENDERS.
  const showInvoiceFrequency = props.clientIsInternal !== true;

  const initial =
    props.mode === 'edit'
      ? props.initialValues
      : {
          employeeId: props.fixedEmployee?.id ?? '',
          clientId: props.fixedClient?.id ?? '',
          startDate: '',
          endDate: null,
          note: null,
          invoiceFrequencyTypeId: null,
        };

  const form = useForm({
    defaultValues: {
      employeeId: String(initial.employeeId),
      clientId: String(initial.clientId),
      startDate: initial.startDate,
      endDate: initial.endDate ?? '',
      note: initial.note ?? '',
      invoiceFrequencyTypeId:
        initial.invoiceFrequencyTypeId === null ? '' : String(initial.invoiceFrequencyTypeId),
    },
    onSubmit: async ({ value }) => {
      const startDate = value.startDate;
      const endDate = value.endDate.trim() === '' ? null : value.endDate;
      const note = value.note.trim() === '' ? null : value.note;

      // Blank means "no override — bill the way the client does", NOT zero. `Number('')` is 0, an id
      // no cadence has, so the empty case has to be mapped explicitly (FR-037).
      const invoiceFrequencyTypeId =
        value.invoiceFrequencyTypeId === '' ? null : Number(value.invoiceFrequencyTypeId);

      const result =
        props.mode === 'create'
          ? await props.onSubmit({
              employeeId: Number(value.employeeId),
              clientId: Number(value.clientId),
              startDate,
              endDate,
              note,
              invoiceFrequencyTypeId,
            })
          : await props.onSubmit({ startDate, endDate, note, invoiceFrequencyTypeId });

      setError(result.kind === 'rejected' ? result.message : null);
    },
  });

  if (props.mode === 'edit' && !props.canEdit) {
    return (
      <dl className="grid grid-cols-1 gap-x-7 gap-y-2 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-xs font-medium uppercase tracking-wide">Start Date</dt>
          <dd className="mt-1">{formatDate(props.initialValues.startDate)}</dd>
        </div>
        <div>
          <dt className="text-xs font-medium uppercase tracking-wide">End Date</dt>
          <dd className="mt-1">
            {props.initialValues.endDate ? formatDate(props.initialValues.endDate) : 'Current'}
          </dd>
        </div>
        {showInvoiceFrequency && (
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide">Invoice frequency</dt>
            <dd className="mt-1">
              {/* The OVERRIDE only. The resolved effective cadence is the row's own
                  `effectiveInvoiceFrequency`, which the detail screen shows; repeating a resolved
                  value inside the edit card would state a conclusion this form cannot itself
                  justify. */}
              {cadenceLabel(props.initialValues.invoiceFrequencyTypeId, activeCadences)}
            </dd>
          </div>
        )}
        <div className="sm:col-span-2">
          <dt className="text-xs font-medium uppercase tracking-wide">Note</dt>
          <dd className="mt-1">{props.initialValues.note ?? '—'}</dd>
        </div>
      </dl>
    );
  }

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        void form.handleSubmit();
      }}
      className="flex flex-col gap-4"
    >
      {error !== null && (
        <p
          role="alert"
          className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
        >
          {error}
        </p>
      )}

      {props.mode === 'create' && (
        <FormGrid>
          {/* `required` only when this side is actually an INPUT. When it is fixed, the child is a
              `<p>`, and `FormField` attaches `aria-required` to whatever it is given — putting that
              attribute on a paragraph is invalid ARIA (axe: `aria-allowed-attr`, critical) and it
              also claims the reader must supply something they cannot. Found by the #84 sweep. */}
          <FormField label="EDJEr" required={props.fixedEmployee === undefined}>
            {(id) =>
              props.fixedEmployee !== undefined ? (
                <p id={id} className="text-sm">
                  {props.fixedEmployee.label}
                </p>
              ) : (
                <form.Field name="employeeId">
                  {(field) =>
                    props.renderEmployeePicker !== undefined ? (
                      props.renderEmployeePicker(field.state.value, field.handleChange, id)
                    ) : (
                      <input
                        id={id}
                        type="number"
                        value={field.state.value}
                        onChange={(event) => field.handleChange(event.target.value)}
                        className={fieldControlClass}
                      />
                    )
                  }
                </form.Field>
              )
            }
          </FormField>

          {/* `required` only when this side is actually an INPUT. When it is fixed, the child is a
              `<p>`, and `FormField` attaches `aria-required` to whatever it is given — putting that
              attribute on a paragraph is invalid ARIA (axe: `aria-allowed-attr`, critical) and it
              also claims the reader must supply something they cannot. Found by the #84 sweep. */}
          <FormField label="Client" required={props.fixedClient === undefined}>
            {(id) =>
              props.fixedClient !== undefined ? (
                <p id={id} className="text-sm">
                  {props.fixedClient.label}
                </p>
              ) : (
                <form.Field name="clientId">
                  {(field) =>
                    props.renderClientPicker !== undefined ? (
                      props.renderClientPicker(field.state.value, field.handleChange, id)
                    ) : (
                      <input
                        id={id}
                        type="number"
                        value={field.state.value}
                        onChange={(event) => field.handleChange(event.target.value)}
                        className={fieldControlClass}
                      />
                    )
                  }
                </form.Field>
              )
            }
          </FormField>
        </FormGrid>
      )}

      <FormGrid>
        <FormField label="Start Date" required>
          {(id) => (
            <form.Field name="startDate">
              {(field) => (
                <input
                  id={id}
                  type="date"
                  value={field.state.value}
                  onChange={(event) => field.handleChange(event.target.value)}
                  className={fieldControlClass}
                />
              )}
            </form.Field>
          )}
        </FormField>

        <FormField label="End Date">
          {(id) => (
            <form.Field name="endDate">
              {(field) => (
                <input
                  id={id}
                  type="date"
                  value={field.state.value}
                  onChange={(event) => field.handleChange(event.target.value)}
                  className={fieldControlClass}
                />
              )}
            </form.Field>
          )}
        </FormField>
      </FormGrid>

      {/* Issue #518 — omitted entirely for an internal client. The form FIELD survives (it is
          still in `defaultValues` and still submitted), so hiding the control cannot clear a
          stored override as a side effect of a display rule. */}
      {showInvoiceFrequency && (
        <FormField label="Invoice frequency">
          {(id) => (
            <form.Field name="invoiceFrequencyTypeId">
              {(field) => (
                <select
                  id={id}
                  value={field.state.value}
                  onChange={(event) => field.handleChange(event.target.value)}
                  className={fieldControlClass}
                >
                  {/* An explicit no-override option, not a blank: "bill like the client" is a
                      choice an engagement makes, and a select that could not express it would force
                      every assignment to carry a cadence of its own (FR-037). */}
                  <option value="">Use the client default</option>
                  {activeCadences.map((cadence) => (
                    <option key={cadence.id} value={String(cadence.id)}>
                      {cadence.typeName}
                    </option>
                  ))}
                  {/* A stored cadence that has since been retired stays selectable HERE so an edit
                      to some other field does not silently clear it. The server applies the same
                      rule — keeping a stored value is not selecting a retired one (FR-038, matching
                      004 US3's client-level behaviour). */}
                  {retiredSelection(field.state.value, activeCadences) && (
                    <option value={field.state.value}>
                      Current cadence (retired, no longer offered)
                    </option>
                  )}
                </select>
              )}
            </form.Field>
          )}
        </FormField>
      )}

      <FormField label="Note">
        {(id) => (
          <form.Field name="note">
            {(field) => (
              <input
                id={id}
                type="text"
                value={field.state.value}
                onChange={(event) => field.handleChange(event.target.value)}
                className={fieldControlClass}
              />
            )}
          </form.Field>
        )}
      </FormField>

      <div>
        <Button type="submit">Save</Button>
      </div>
    </form>
  );
}

/**
 * The label for a stored override, for the read-only view.
 *
 * Falls back to naming the id when the cadence is no longer active: it is still the cadence this
 * assignment bills on, and rendering an empty cell would say the opposite.
 */
function cadenceLabel(
  invoiceFrequencyTypeId: number | null,
  activeCadences: { id: number; typeName: string }[],
): string {
  if (invoiceFrequencyTypeId === null) {
    return 'Client default';
  }

  const match = activeCadences.find((cadence) => cadence.id === invoiceFrequencyTypeId);
  return match?.typeName ?? 'Retired cadence';
}

/**
 * Whether the selected cadence is one the active-only list does not offer.
 *
 * True means this assignment carries a retired override, which the select has to keep as an option or
 * the next save of any unrelated field would silently clear it. Mirrors `EdjerFormPage`'s handling of
 * a retired employee type, and `ClientFormPage`'s of a retired client-level default.
 */
function retiredSelection(selected: string, activeCadences: { id: number }[]): boolean {
  return selected !== '' && !activeCadences.some((cadence) => String(cadence.id) === selected);
}
