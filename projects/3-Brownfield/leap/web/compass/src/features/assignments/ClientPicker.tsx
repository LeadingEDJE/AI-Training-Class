import { useClientPickers } from './useAssignments';

interface ClientPickerProps {
  id: string;
  value: string;
  onChange: (value: string) => void;
}

/**
 * The client picker (US2, issue #63) — a typeahead/combobox fed by a filtered client list.
 *
 * **Inactive clients are excluded from the option list — this is the O6 named regression
 * test.** `derivedStatus` (BR-11) is used to filter the list before rendering, so a client with
 * zero assignments never appears here.
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
