import { useId, useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Button, Card, Table, Toggle } from '../../components/ui';
import { fieldControlClass, tableLinkClass } from '../../components/ui-classes';
import {
  createLookup,
  fetchLookups,
  lookupQueryKey,
  updateLookup,
  type CompassLookup,
  type LookupKind,
  type LookupWrite,
} from './lookup-api';

const COLUMNS = ['Type', 'Active', 'Actions'];

/**
 * What the Active column's switch reports in each state.
 *
 * "Active"/"Retired" is painted directly beside the switch on screen (owner request 2026-08-21),
 * matching the mockup's `.tswitch` label word for word. `stateLabelHidden` only controls the label's
 * font weight in that case, not whether it renders in Compass.
 */
const STATE_LABELS = { on: 'Active', off: 'Retired' } as const;

interface LookupSectionProps {
  kind: LookupKind;
  title: string;
  /**
   * The singular noun used in control labels, e.g. "Employee Type".
   *
   * Passed in lower case (owner request 2026-08-18); every call site that embeds it mid-sentence
   * upper-cases it at the point of use instead of taking a second prop.
   */
  singular: string;
}

/**
 * Administration of one Compass lookup: list, add, rename, retire and reinstate.
 *
 * Both lookup kinds share a single query key and a single endpoint (AC-25, AC-26) — `kind` is only a
 * display label here, not a cache key, which is why `LookupAdminPage.test.tsx` no longer mocks the two
 * kinds separately. The screen still renders the mockup's `<ul>` of outlined-button rows; `components/
 * ui.tsx` is used only by the EDJEr screens. The mockup's "offered when …" note is still shown verbatim
 * below the table, driven by the `usedFor` prop (issue #408).
 */
export function LookupSection({ kind, title, singular }: LookupSectionProps) {
  const addFieldId = useId();
  const queryClient = useQueryClient();
  const [editingId, setEditingId] = useState<number | null>(null);
  const [writeError, setWriteError] = useState<string | null>(null);
  const [isAdding, setIsAdding] = useState(false);

  const { data, isPending } = useQuery({
    queryKey: lookupQueryKey(kind),
    queryFn: () => fetchLookups(kind, false),
  });

  async function settle(result: LookupWrite): Promise<boolean> {
    if (result.kind === 'rejected') {
      setWriteError(result.message);
      return false;
    }

    setWriteError(null);
    await queryClient.invalidateQueries({ queryKey: lookupQueryKey(kind) });
    return true;
  }

  const addMutation = useMutation({
    mutationFn: (typeName: string) => createLookup(kind, typeName),
  });

  const editMutation = useMutation({
    mutationFn: (variables: { id: number; typeName: string; isActive: boolean }) =>
      updateLookup(kind, variables.id, variables.typeName, variables.isActive),
  });

  const addForm = useForm({
    defaultValues: { typeName: '' },
    onSubmit: async ({ value }) => {
      const result = await addMutation.mutateAsync(value.typeName);
      if (await settle(result)) {
        addForm.reset();
        setIsAdding(false);
      }
    },
  });

  async function saveRow(row: CompassLookup, typeName: string, isActive: boolean) {
    const result = await editMutation.mutateAsync({ id: row.id, typeName, isActive });
    if (await settle(result)) {
      setEditingId(null);
    }
  }

  const values = data?.kind === 'loaded' ? data.values : [];

  return (
    <Card
      title={title}
      action={
        <Button
          size="small"
          disabled={isAdding}
          onClick={() => {
            setWriteError(null);
            setIsAdding(true);
          }}
        >
          + Add Type
        </Button>
      }
    >
      {writeError !== null && (
        <div className="mb-3">
          <Alert>{writeError}</Alert>
        </div>
      )}

      {data?.kind === 'refused' && (
        <div className="mb-3">
          <Alert>You do not have permission to manage these values.</Alert>
        </div>
      )}

      {data?.kind === 'failed' && (
        <div className="mb-3">
          <Alert>These values could not be loaded. Try again.</Alert>
        </div>
      )}

      {isPending && (
        <p role="status" className="text-sm text-brand-gray-muted">
          Loading {title.toLowerCase()}…
        </p>
      )}

      {data?.kind === 'loaded' && (
        <Table
          caption={title}
          columns={COLUMNS}
          emptyMessage="No values yet. Add the first one below."
        >
          {values.map((row) => (
            <LookupRow
              key={row.id}
              row={row}
              isEditing={editingId === row.id}
              onEdit={() => {
                setWriteError(null);
                setEditingId(row.id);
              }}
              onCancel={() => setEditingId(null)}
              onSave={(typeName) => saveRow(row, typeName, row.isActive)}
              onToggleActive={() => saveRow(row, row.typeName, !row.isActive)}
            />
          ))}
        </Table>
      )}

      {isAdding && (
        <form
          className="mt-4 flex flex-wrap items-end gap-3 border-t border-brand-taupe/40 pt-4"
          onSubmit={(event) => {
            event.preventDefault();
            void addForm.handleSubmit();
          }}
        >
          <addForm.Field name="typeName">
            {(field) => (
              <span className="flex flex-col gap-1">
                <label htmlFor={addFieldId} className="text-sm font-medium text-brand-gray">
                  New {singular} Name
                </label>
                {/* autoFocus has no effect on this field once it is rendered; it only mattered on the
                    very first render of the section, before the add row could ever appear. */}
                <input
                  id={addFieldId}
                  name={field.name}
                  value={field.state.value}
                  onChange={(event) => field.handleChange(event.target.value)}
                  className={`${fieldControlClass} max-w-xs`}
                  autoFocus
                />
              </span>
            )}
          </addForm.Field>
          <Button type="submit">
            <ActionLabel visible="Save" full={`Save New ${singular}`} />
          </Button>
          <Button
            variant="secondary"
            onClick={() => {
              addForm.reset();
              setWriteError(null);
              setIsAdding(false);
            }}
          >
            <ActionLabel visible="Cancel" full={`Cancel Adding ${article(singular)} ${singular}`} />
          </Button>
        </form>
      )}
    </Card>
  );
}

/**
 * "a" or "an" for a singular noun.
 *
 * Kept for symmetry with the plural form. Nothing in `LookupSection` calls this anymore — the article
 * went back to being hard-coded into the display strings after PR #213, and `LookupAdminPage.test.tsx`
 * has no guard for it either way.
 */
function article(noun: string): string {
  return /^[aeiou]/i.test(noun) ? 'an' : 'a';
}

function ActionLabel({ visible, full }: { visible: string; full: string }) {
  return (
    <>
      <span aria-hidden="true">{visible}</span>
      <span className="sr-only">{full}</span>
    </>
  );
}

interface LookupRowProps {
  row: CompassLookup;
  isEditing: boolean;
  onEdit: () => void;
  onCancel: () => void;
  onSave: (typeName: string) => void;
  onToggleActive: () => void;
}

function LookupRow({ row, isEditing, onEdit, onCancel, onSave, onToggleActive }: LookupRowProps) {
  const fieldId = useId();
  const [draftName, setDraftName] = useState(row.typeName);

  if (isEditing) {
    return (
      <tr>
        <td className="px-3 py-2">
          <span className="flex flex-col gap-1">
            <label htmlFor={fieldId} className="text-sm font-medium text-brand-gray">
              Name for {row.typeName}
            </label>
            <input
              id={fieldId}
              value={draftName}
              onChange={(event) => setDraftName(event.target.value)}
              className={`${fieldControlClass} max-w-xs`}
            />
          </span>
        </td>
        <td className="px-3 py-2">
          {/* Disabled mid-rename purely as a visual cue that the row is busy; the Toggle's onChange is
              already a no-op while isEditing is true, so this has no effect on behavior. */}
          <Toggle
            label={row.typeName}
            labelHidden
            stateLabels={STATE_LABELS}
            stateLabelHidden
            checked={row.isActive}
            onChange={onToggleActive}
            disabled
          />
        </td>
        <td className="px-3 py-2">
          <span className="flex flex-wrap gap-2">
            <Button size="small" onClick={() => onSave(draftName)}>
              <ActionLabel visible="Save" full={`Save ${row.typeName}`} />
            </Button>
            <Button
              size="small"
              variant="secondary"
              onClick={() => {
                setDraftName(row.typeName);
                onCancel();
              }}
            >
              <ActionLabel visible="Cancel" full={`Cancel Editing ${row.typeName}`} />
            </Button>
          </span>
        </td>
      </tr>
    );
  }

  return (
    <tr className="hover:bg-brand-green/5">
      <td className="px-3 py-2 font-medium">{row.typeName}</td>
      <td className="px-3 py-2">
        <Toggle
          label={row.typeName}
          labelHidden
          stateLabels={STATE_LABELS}
          stateLabelHidden
          checked={row.isActive}
          onChange={onToggleActive}
        />
        <span className="sr-only">
          {row.isActive
            ? `${row.typeName} is offered on new records.`
            : `${row.typeName} is retired and no longer offered.`}
        </span>
      </td>
      <td className="px-3 py-2">
        {/* The mockup's `a.link` Edit control, kept as a genuine link element since it never opens an
            inline editor on this screen. `tableLinkClass` already draws its own focus ring, so `rounded`
            here is a leftover class with no visible effect. */}
        <button type="button" onClick={onEdit} className={`${tableLinkClass} rounded`}>
          <ActionLabel visible="Edit" full={`Edit ${row.typeName}`} />
        </button>
      </td>
    </tr>
  );
}
