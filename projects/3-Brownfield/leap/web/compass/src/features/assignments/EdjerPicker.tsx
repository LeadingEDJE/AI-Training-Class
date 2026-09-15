import { useEdjerPickers } from './useAssignments';

interface EdjerPickerProps {
  id: string;
  value: string;
  onChange: (value: string) => void;
}

/**
 * The EDJEr picker, symmetric to {@link ClientPicker} — a plain `<select>` fed by every EDJEr
 * regardless of active status. Used from the "new assignment" flow reached via an EDJEr's own
 * record, where the EDJEr side is fixed and the client side needs picking.
 */
export function EdjerPicker({ id, value, onChange }: EdjerPickerProps) {
  const { data: edjers = [], isPending } = useEdjerPickers();

  return (
    <select
      id={id}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      disabled={isPending}
      className="w-full rounded border border-brand-gray/70 bg-white px-2 py-1.5 text-sm"
    >
      <option value="">{isPending ? 'Loading EDJErs…' : 'Select an EDJEr'}</option>
      {edjers.map((edjer) => (
        <option key={edjer.id} value={edjer.id}>
          {edjer.displayName}
        </option>
      ))}
    </select>
  );
}
