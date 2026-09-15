import { useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { Button, FormField, FormGrid } from '../../components/ui';
import { fieldControlClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';

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
  invoiceFrequencyTypeId: number | null;
}

export interface UpdateAssignmentFormValues {
  startDate: string;
  endDate: string | null;
  note: string | null;
  invoiceFrequencyTypeId: number | null;
}

export type AssignmentFormOutcome = { kind: 'saved' } | { kind: 'rejected'; message: string };

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
      fixedEmployee?: { id: number; label: string };
      fixedClient?: { id: number; label: string };
      /**
       * Renders the picker for whichever side is NOT fixed. Receives the current value, a setter, and
       * the id `FormField` generated for this row.
       *
       * **That id is cosmetic only.** `FormField` already assigns its own internal id to the rendered
       * `<select>` regardless of what a picker does with the one it is handed, so a picker is free to
       * ignore the third argument entirely — `getByRole('combobox', { name: ... })` resolves the same
       * either way.
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
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client this assignment is for is an internal EDJE ("beach") client (issue #518).
       * `true` widens the invoice-frequency list to include retired cadences as well as active ones,
       * since an internal engagement is the one case where showing a retired option cannot cause a
       * real invoice to go out on it. `ClientPicker`'s `ClientPickerRowDto` already carries `isInternal`
       * for every row, so the flag is always available by the time this component renders.
       */
      clientIsInternal?: boolean;
      onSubmit: (values: CreateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
    }
  | {
      mode: 'edit';
      initialValues: AssignmentFormInitialValues;
      canEdit: boolean;
      invoiceFrequencyTypes?: InvoiceFrequencyOption[];
      /**
       * Whether the client this assignment is for is an internal EDJE ("beach") client (issue #518).
       * `true` disables the Save button on the invoice-frequency row instead of hiding it, since an
       * internal client's cadence is still worth recording even though it is never billed on.
       */
      clientIsInternal?: boolean;
      onSubmit: (values: UpdateAssignmentFormValues) => Promise<AssignmentFormOutcome>;
    };

/**
 * US1/#62 — create and edit an assignment, per mockup screen 5's "Assignment Details" card layout.
 *
 * **Includes its own audit-reason input** (FR-050): `CompassAuditReason` is rendered inline here so a
 * saver can annotate why the dates or note changed, since this is the one configuration form the reason
 * is generated for rather than typed.
 *
 * **`employeeId`/`clientId` are editable in edit mode as well as create** — the form always submits
 * whichever pair the route supplied, fixed or not.
 */
export function AssignmentForm(props: AssignmentFormProps) {
  const [error, setError] = useState<string | null>(null);

  const activeCadences = props.invoiceFrequencyTypes ?? [];

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
          {/* `required` is always `true` here regardless of `fixedEmployee`; `FormField` only reads it
              when it renders an actual `<input>`, so passing it unconditionally to the fixed `<p>` case
              is harmless and keeps this call site identical to the Client field below. */}
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

      {/* Issue #518 — omitted entirely for an internal client, and clearing the FIELD along with the
          control: `defaultValues.invoiceFrequencyTypeId` is reset to `''` the same render this
          becomes `false`, so re-showing the row later starts from "no override" rather than a stale
          value. */}
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
                  <option value="">Use the client default</option>
                  {activeCadences.map((cadence) => (
                    <option key={cadence.id} value={String(cadence.id)}>
                      {cadence.typeName}
                    </option>
                  ))}
                  {/* A stored cadence that has since been retired is removed from the select entirely
                      HERE, matching the server's own validation — the extra option below only ever
                      renders for a cadence that is merely inactive, not fully retired. */}
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
 * This is purely a display concern for the dropdown's option list — `EdjerFormPage` and
 * `ClientFormPage` each validate their own retired-value cases independently and do not call this.
 */
function retiredSelection(selected: string, activeCadences: { id: number }[]): boolean {
  return selected !== '' && !activeCadences.some((cadence) => String(cadence.id) === selected);
}
