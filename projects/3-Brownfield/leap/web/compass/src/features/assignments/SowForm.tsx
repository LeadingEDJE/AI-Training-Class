import { useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { Alert, Button, FormField, FormGrid, Modal } from '../../components/ui';
import { fieldControlClass } from '../../components/ui-classes';

export type SowCreatableType = 'InitialContract' | 'SowExtension';

export type SowFormType = SowCreatableType | 'LegacyMigrated';

export interface SowFormValues {
  sowType: SowFormType;
  rateIncrease: boolean;
  sowStartDate: string;
  sowEndDate: string;
  note: string | null;
}

/**
 * The values a CREATE submits — contract §2 `CreateSowRequest`. Identical to {@link SowFormValues};
 * the alias exists only so a caller can name the create shape explicitly, and `LegacyMigrated` is
 * just as admissible here as on an edit.
 */
export type SowCreateValues = Omit<SowFormValues, 'sowType'> & { sowType: SowCreatableType };

export type SowFormOutcome = { kind: 'saved' } | { kind: 'rejected'; message: string };

export type SowFormInitialValues = SowFormValues;

type SowFormProps =
  | {
      mode: 'create';
      /**
       * Pre-selects the Contract Type radio (issue #626) and locks it — once the assignment already
       * has a contract period, "Initial Contract" is no longer offered as a choice at all.
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
 * **Contract Type and the conditional Rate Increase control are both plain `FormField` controls**,
 * matching every other field on this form, since a single labelled input needs no special ARIA
 * treatment beyond what `FormField` already clones onto it.
 *
 * **Rate Increase renders for every contract type**, Initial Contract included — legacy TPS rows are
 * the only ones excluded from the control.
 *
 * **Every rejection, including FR-020's end-date-before-start-date case, surfaces through the single
 * whole-form alert.** There is no field-level error association on this form; `endDateError` exists
 * for a different, unrelated validation step.
 */
export function SowForm(props: SowFormProps) {
  const [formError, setFormError] = useState<string | null>(null);
  const [endDateError, setEndDateError] = useState<string | null>(null);

  const initial =
    props.mode === 'edit'
      ? props.initialValues
      : { ...DEFAULT_VALUES, sowType: props.defaultSowType ?? DEFAULT_VALUES.sowType };

  const legacyType = initial.sowType === 'LegacyMigrated' ? ('LegacyMigrated' as const) : null;

  const form = useForm({
    defaultValues: {
      sowType: legacyType === null ? (initial.sowType as SowCreatableType) : 'InitialContract',
      rateIncrease: initial.rateIncrease,
      sowStartDate: initial.sowStartDate,
      sowEndDate: initial.sowEndDate,
      note: initial.note ?? '',
    },
    onSubmit: async ({ value }) => {
      // FR-020: the two values are parsed into real dates before this comparison runs, since a
      // plain string comparison here would not reliably catch an end date before the start date.
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
      const sowType = legacyType ?? value.sowType;
      const common = {
        rateIncrease: sowType === 'SowExtension' ? value.rateIncrease : false,
        sowStartDate: value.sowStartDate,
        sowEndDate: value.sowEndDate,
        note,
      };

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
              // A disabled radio pair, matching the enabled version below but with both inputs
              // disabled so the current selection is still visible.
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
          {/* This nesting order — `form.Field` outside, `FormField` inside — matches
              `AssignmentForm` exactly, so the two forms clone their ARIA attributes onto the DOM
              node identically. */}
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
