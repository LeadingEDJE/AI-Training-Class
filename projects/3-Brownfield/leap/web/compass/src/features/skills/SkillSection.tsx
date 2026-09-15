import { useId, useState } from 'react';
import { useForm } from '@tanstack/react-form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Button, Card, Table, Toggle } from '../../components/ui';
import { fieldControlClass, tableLinkClass } from '../../components/ui-classes';
import {
  createSkill,
  fetchSkills,
  skillOptionsQueryKey,
  skillsQueryKey,
  updateSkill,
  type CompassSkill,
  type SkillWrite,
} from './skill-api';

const COLUMNS = ['Name', 'Active', 'Actions'];

const STATE_LABELS = { on: 'Active', off: 'Retired' } as const;

/**
 * Administration of the master skill list: list, add, rename, retire and reinstate.
 *
 * Mirrors {@link import('../lookups/LookupSection').LookupSection} in shape — same discriminated-union
 * load/write outcomes, same reveal-on-Add row — with one resource instead of a `kind` parameter, since
 * there is only one skill collection.
 */
export function SkillSection() {
  const addFieldId = useId();
  const queryClient = useQueryClient();
  const [editingId, setEditingId] = useState<number | null>(null);
  const [writeError, setWriteError] = useState<string | null>(null);
  const [isAdding, setIsAdding] = useState(false);

  const { data, isPending } = useQuery({ queryKey: skillsQueryKey(), queryFn: fetchSkills });

  async function settle(result: SkillWrite): Promise<boolean> {
    if (result.kind === 'rejected') {
      setWriteError(result.message);
      return false;
    }

    setWriteError(null);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: skillsQueryKey() }),
      // The Team Directory filter's option list reads the public, active-only endpoint — a rename or
      // retire here must reach it too, or the dropdown shows a stale name after this screen updates it.
      queryClient.invalidateQueries({ queryKey: skillOptionsQueryKey() }),
    ]);
    return true;
  }

  const addMutation = useMutation({ mutationFn: (name: string) => createSkill(name) });

  const editMutation = useMutation({
    mutationFn: (variables: { id: number; name: string; isActive: boolean }) =>
      updateSkill(variables.id, variables.name, variables.isActive),
  });

  const addForm = useForm({
    defaultValues: { name: '' },
    onSubmit: async ({ value }) => {
      const result = await addMutation.mutateAsync(value.name);
      if (await settle(result)) {
        addForm.reset();
        setIsAdding(false);
      }
    },
  });

  async function saveRow(row: CompassSkill, name: string, isActive: boolean) {
    const result = await editMutation.mutateAsync({ id: row.id, name, isActive });
    if (await settle(result)) {
      setEditingId(null);
    }
  }

  const values = data?.kind === 'loaded' ? data.values : [];

  return (
    <Card
      title="Skills"
      action={
        <Button
          size="small"
          disabled={isAdding}
          onClick={() => {
            setWriteError(null);
            setIsAdding(true);
          }}
        >
          + Add Skill
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
          Loading skills…
        </p>
      )}

      {data?.kind === 'loaded' && (
        <Table caption="Skills" columns={COLUMNS} emptyMessage="No skills yet. Add the first one below.">
          {values.map((row) => (
            <SkillRow
              key={row.id}
              row={row}
              isEditing={editingId === row.id}
              onEdit={() => {
                setWriteError(null);
                setEditingId(row.id);
              }}
              onCancel={() => setEditingId(null)}
              onSave={(name) => saveRow(row, name, row.isActive)}
              onToggleActive={() => saveRow(row, row.name, !row.isActive)}
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
          <addForm.Field name="name">
            {(field) => (
              <span className="flex flex-col gap-1">
                <label htmlFor={addFieldId} className="text-sm font-medium text-brand-gray">
                  New Skill Name
                </label>
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
            <ActionLabel visible="Save" full="Save New Skill" />
          </Button>
          <Button
            variant="secondary"
            onClick={() => {
              addForm.reset();
              setWriteError(null);
              setIsAdding(false);
            }}
          >
            <ActionLabel visible="Cancel" full="Cancel Adding a Skill" />
          </Button>
        </form>
      )}
    </Card>
  );
}

function ActionLabel({ visible, full }: { visible: string; full: string }) {
  return (
    <>
      <span aria-hidden="true">{visible}</span>
      <span className="sr-only">{full}</span>
    </>
  );
}

interface SkillRowProps {
  row: CompassSkill;
  isEditing: boolean;
  onEdit: () => void;
  onCancel: () => void;
  onSave: (name: string) => void;
  onToggleActive: () => void;
}

function SkillRow({ row, isEditing, onEdit, onCancel, onSave, onToggleActive }: SkillRowProps) {
  const fieldId = useId();
  const [draftName, setDraftName] = useState(row.name);

  if (isEditing) {
    return (
      <tr>
        <td className="px-3 py-2">
          <span className="flex flex-col gap-1">
            <label htmlFor={fieldId} className="text-sm font-medium text-brand-gray">
              Name for {row.name}
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
          <Toggle
            label={row.name}
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
              <ActionLabel visible="Save" full={`Save ${row.name}`} />
            </Button>
            <Button
              size="small"
              variant="secondary"
              onClick={() => {
                setDraftName(row.name);
                onCancel();
              }}
            >
              <ActionLabel visible="Cancel" full={`Cancel Editing ${row.name}`} />
            </Button>
          </span>
        </td>
      </tr>
    );
  }

  return (
    <tr className="hover:bg-brand-green/5">
      <td className="px-3 py-2 font-medium">{row.name}</td>
      <td className="px-3 py-2">
        <Toggle
          label={row.name}
          labelHidden
          stateLabels={STATE_LABELS}
          stateLabelHidden
          checked={row.isActive}
          onChange={onToggleActive}
        />
        <span className="sr-only">
          {row.isActive ? `${row.name} is offered on new records.` : `${row.name} is retired and no longer offered.`}
        </span>
      </td>
      <td className="px-3 py-2">
        <button type="button" onClick={onEdit} className={`${tableLinkClass} rounded`}>
          <ActionLabel visible="Edit" full={`Edit ${row.name}`} />
        </button>
      </td>
    </tr>
  );
}
