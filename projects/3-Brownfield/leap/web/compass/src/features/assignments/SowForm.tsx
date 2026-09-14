import { useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { Alert, Button, FormField, FormGrid, Modal } from '../../components/ui';
import { fieldControlClass } from '../../components/ui-classes';

/** The two types a caller may CREATE. `LegacyMigrated` cannot be chosen (FR-016). */
export type SowCreatableType = 'InitialContract' | 'SowExtension';

/**
 * The types the form may hold. `LegacyMigrated` is reachable only by EDITING a row that already is
 * one (US8/#67, issue #404) — it is never offered as a choice, and it rides back to the server
 * unchanged, because a legacy row's type is immutable in both directions.
 */
export type SowFormType = SowCreatableType | 'LegacyMigrated';

/** The values an EDIT submits — contract §2 `UpdateSowRequest`, so `LegacyMigrated` is admissible. */
export interface SowFormValues {
  sowType: SowFormType;
  rateIncrease: boolean;
  sowStartDate: string;
  sowEndDate: string;
  note: string | null;
}

/**
 * The values a CREATE submits — contract §2 `CreateSowRequest`. Narrower than {@link SowFormValues}
 * by exactly `LegacyMigrated`, which a create may never name (FR-016). Keeping the two apart is what
 * stops the legacy type leaking into `createSow`, and the compiler enforces it at the call site.
 */
export type SowCreateValues = Omit<SowFormValues, 'sowType'> & { sowType: SowCreatableType };

/** The outcome of a submit, mirroring `AssignmentFormOutcome`. */
export type SowFormOutcome = { kind: 'saved' } | { kind: 'rejected'; message: string };

/** The existing values an edit pre-populates from. */
export type SowFormInitialValues = SowFormValues;

type SowFormProps =
  | {
      mode: 'create';
      /**
       * Pre-selects the Contract Type radio (issue #626) — "SOW Extension" when the assignment
       * already has at least one contract period, else "Initial Contract". A starting point only:
       * the person adding it can still switch it back before saving.
       */
      defaultSowType?: SowCreatableType;
      onSubmit: (values: SowCreateValues) => Promise<SowFormOutcome>;
      onCancel: () => void;
    }
  | {
      mode: 'edit';
      initialValues: SowFormInitialValues;
      onSubmit: (values: SowFormValues) => Promise<SowFormOutcome>;
      onCancel: () => void;
    };

const DEFAULT_VALUES: SowFormValues = {
  sowType: 'InitialContract',
  rateIncrease: false,
  sowStartDate: '',
  sowEndDate: '',
  note: null,
};

/**
 * US3/#66 — create and edit a contract period, as a real modal per mockup screen 6 ("Add SOW").
 *
 * **Contract Type and the conditional Rate Increase control are radio groups**, not `FormField`
 * controls — `FormField` clones a SINGLE control to attach ARIA attributes, which does not fit a
 * group of inputs sharing one name. Each group is a labelled `fieldset`/`legend` instead, which is
 * the native way a screen reader learns a set of radios belongs together.
 *
 * **Rate Increase renders ONLY for a SOW Extension** (SOW-2) — legacy TPS tracks no extensions at
 * all, so the control has no meaning for an Initial Contract.
 *
 * **FR-020 (end date before start date) is caught CLIENT-SIDE and associated with the SOW End Date
 * field itself**, not the whole-form alert — research R-6's field-level error-association pattern,
 * established here with no precedent to inherit (FR-051c, SC-010b). This is a courtesy: the server
 * remains the validation authority, and every OTHER rejection (overlap, rate-increase-on-non-
 * extension, `LegacyMigrated` refused, or a race-condition date-order slip) surfaces through the
 * single whole-form alert, since none of them belongs to one field the way end-before-start does.
 *
 * **Renders NO audit-reason input** (FR-050): reasons are system-generated.
 */
export function SowForm(props: SowFormProps) {
  const [formError, setFormError] = useState<string | null>(null);
  const [endDateError, setEndDateError] = useState<string | null>(null);

  const initial =
    props.mode === 'edit'
      ? props.initialValues
      : { ...DEFAULT_VALUES, sowType: props.defaultSowType ?? DEFAULT_VALUES.sowType };

  /**
   * A legacy row's type is IMMUTABLE -- the server refuses a change in both directions -- so it is
   * deliberately not part of form state. Keeping it out is what lets the form's own `sowType` stay
   * narrowed to the two creatable types, which in turn lets the create branch below type-check
   * WITHOUT a cast: a future path that could set `LegacyMigrated` in create mode would fail to
   * compile rather than silently POST an invalid create.
   */
  const legacyType = initial.sowType === 'LegacyMigrated' ? ('LegacyMigrated' as const) : null;

  const form = useForm({
    defaultValues: {
      // Narrow by construction. For a legacy row this seeds an unused placeholder: the control is
      // read-only, and `legacyType` -- not this -- is what gets submitted.
      sowType: legacyType === null ? (initial.sowType as SowCreatableType) : 'InitialContract',
      rateIncrease: initial.rateIncrease,
      sowStartDate: initial.sowStartDate,
      sowEndDate: initial.sowEndDate,
      note: initial.note ?? '',
    },
    onSubmit: async ({ value }) => {
      // FR-020, research R-6: caught here as a courtesy, associated with the field it belongs to.
      // ISO yyyy-mm-dd strings compare correctly lexicographically, so no Date parsing is needed.
      if (
        value.sowEndDate !== '' &&
        value.sowStartDate !== '' &&
        value.sowEndDate < value.sowStartDate
      ) {
        setEndDateError('The end date must be on or after the start date.');
        return;
      }
      setEndDateError(null);

      const note = value.note.trim() === '' ? null : value.note;
      // The type actually being submitted: a legacy row's rides back unchanged, everyone else's
      // comes from the radios.
      const sowType = legacyType ?? value.sowType;
      const common = {
        // A non-Extension can never carry a rate increase — enforced structurally by the
        // conditional control below, not merely by trusting the value that arrives here.
        rateIncrease: sowType === 'SowExtension' ? value.rateIncrease : false,
        sowStartDate: value.sowStartDate,
        sowEndDate: value.sowEndDate,
        note,
      };

      // Branched on the mode rather than cast. Each mode's `onSubmit` takes a different width, and
      // calling them through a union would demand the NARROWER one; a cast would silence that and
      // take the create path's compile-time guarantee with it. Both branches are exercised — the
      // create tests and the edit tests — so this costs no coverage.
      const result =
        props.mode === 'edit'
          ? await props.onSubmit({ ...common, sowType })
          : await props.onSubmit({ ...common, sowType: value.sowType });

      setFormError(result.kind === 'rejected' ? result.message : null);
    },
  });

  return (
    <Modal
      title={props.mode === 'create' ? 'Add SOW / Contract' : 'Edit SOW / Contract'}
      onClose={props.onCancel}
      footer={
        <>
          <Button variant="secondary" onClick={props.onCancel}>
            Cancel
          </Button>
          <Button variant="primary" onClick={() => void form.handleSubmit()}>
            Save SOW
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {formError !== null && <Alert>{formError}</Alert>}

        <form.Field name="sowType">
          {(field) =>
            legacyType !== null ? (
              // READ-ONLY, not a disabled radio pair. The server refuses a type change on a legacy row
              // in BOTH directions -- provenance is not editable -- so presenting the choice at all
              // would offer something every save rejects. Rendered as text with the reason beside it,
              // and the value still travels back on submit because it stays in form state.
              <div className="flex flex-col gap-1">
                <span className="text-sm font-medium text-brand-gray">Contract Type</span>
                <span className="text-sm">Legacy Migrated</span>
                <span className="text-xs text-brand-gray">
                  Loaded from TPS, so its type cannot be changed. Correcting the dates or note here
                  puts this period through the full validation rules for the first time.
                </span>
              </div>
            ) : (
              <fieldset className="flex flex-col gap-1">
                <legend className="text-sm font-medium text-brand-gray">
                  Contract Type{' '}
                  <span aria-hidden="true" className="text-brand-danger">
                    *
                  </span>
                </legend>
                <div className="flex gap-4 text-sm">
                  <label className="flex items-center gap-1.5 font-normal">
                    <input
                      type="radio"
                      name="sowType"
                      checked={field.state.value === 'InitialContract'}
                      onChange={() => field.handleChange('InitialContract')}
                    />
                    Initial Contract
                  </label>
                  <label className="flex items-center gap-1.5 font-normal">
                    <input
                      type="radio"
                      name="sowType"
                      checked={field.state.value === 'SowExtension'}
                      onChange={() => field.handleChange('SowExtension')}
                    />
                    SOW Extension
                  </label>
                </div>
              </fieldset>
            )
          }
        </form.Field>

        <form.Subscribe selector={(state) => state.values.sowType}>
          {(sowType) =>
            sowType === 'SowExtension' && (
              <form.Field name="rateIncrease">
                {(field) => (
                  <fieldset className="flex flex-col gap-1">
                    <legend className="text-sm font-medium text-brand-gray">
                      Rate Increase Achieved?
                    </legend>
                    <div className="flex gap-4 text-sm">
                      <label className="flex items-center gap-1.5 font-normal">
                        <input
                          type="radio"
                          name="rateIncrease"
                          checked={field.state.value}
                          onChange={() => field.handleChange(true)}
                        />
                        Yes
                      </label>
                      <label className="flex items-center gap-1.5 font-normal">
                        <input
                          type="radio"
                          name="rateIncrease"
                          checked={!field.state.value}
                          onChange={() => field.handleChange(false)}
                        />
                        No
                      </label>
                    </div>
                    <span className="text-xs text-brand-gray-muted">
                      Rates themselves are not tracked
                    </span>
                  </fieldset>
                )}
              </form.Field>
            )
          }
        </form.Subscribe>

        <FormGrid>
          {/* `form.Field` wraps `FormField` here, NOT the reverse — `FormField` clones its
              IMMEDIATE child to attach `aria-required`/`aria-invalid`/`aria-describedby` (see its
              own docstring), and that child must be the real `<input>`. Nesting `form.Field`
              INSIDE `FormField`'s children, as `AssignmentForm` does throughout, clones those
              attributes onto the render-prop wrapper instead of the DOM node — invisible until a
              field actually carries an `error`, which no existing caller has done before now
              (research R-6). */}
          <form.Field name="sowStartDate">
            {(field) => (
              <FormField label="SOW Start Date" required>
                {(id) => (
                  <input
                    id={id}
                    type="date"
                    value={field.state.value}
                    onChange={(event) => field.handleChange(event.target.value)}
                    className={fieldControlClass}
                  />
                )}
              </FormField>
            )}
          </form.Field>

          <form.Field name="sowEndDate">
            {(field) => (
              <FormField label="SOW End Date" required error={endDateError ?? undefined}>
                {(id) => (
                  <input
                    id={id}
                    type="date"
                    value={field.state.value}
                    onChange={(event) => {
                      field.handleChange(event.target.value);
                      setEndDateError(null);
                    }}
                    className={fieldControlClass}
                  />
                )}
              </FormField>
            )}
          </form.Field>
        </FormGrid>

        <form.Field name="note">
          {(field) => (
            <FormField label="Note">
              {(id) => (
                <textarea
                  id={id}
                  rows={2}
                  value={field.state.value}
                  onChange={(event) => field.handleChange(event.target.value)}
                  className={fieldControlClass}
                />
              )}
            </FormField>
          )}
        </form.Field>
      </div>
    </Modal>
  );
}
