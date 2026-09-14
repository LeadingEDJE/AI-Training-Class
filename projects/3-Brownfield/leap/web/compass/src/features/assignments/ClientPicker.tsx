import { useClientPickers } from './useAssignments';

interface ClientPickerProps {
  id: string;
  value: string;
  onChange: (value: string) => void;
}

/**
 * The client picker (US2, issue #63) — a plain `<select>` fed by the full, unfiltered client list,
 * matching the only entity-picker idiom already in this codebase (`EdjerFormPage.tsx`'s coach and
 * employee-type selects): a fully-loaded `useQuery` list rendered as `<option>`s, not a
 * typeahead/combobox.
 *
 * **Every client is selectable, regardless of derived status — this is the O6 named regression
 * test.** A brand-new client with zero assignments derives Inactive (BR-11) and must still be
 * assignable (FR-009–FR-012); `derivedStatus` is shown as parenthetical CONTEXT ONLY, never used to
 * filter or disable an option.
 */
export function ClientPicker({ id, value, onChange }: ClientPickerProps) {
  const { data: clients = [], isPending } = useClientPickers();

  return (
    <select
      id={id}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      disabled={isPending}
      className="w-full rounded border border-brand-gray/70 bg-white px-2 py-1.5 text-sm"
    >
      <option value="">{isPending ? 'Loading clients…' : 'Select a client'}</option>
      {clients.map((client) => (
        <option key={client.id} value={client.id}>
          {client.clientName} ({client.derivedStatus})
        </option>
      ))}
    </select>
  );
}
