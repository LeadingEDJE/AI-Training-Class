import { useEdjerPickers } from './useAssignments';

interface EdjerPickerProps {
  id: string;
  value: string;
  onChange: (value: string) => void;
}

/**
 * The EDJEr picker (US2, issue #63), symmetric to {@link ClientPicker} — a plain `<select>` fed by
 * the full ACTIVE-EDJEr list (FR-003), matching the same `EdjerFormPage.tsx` idiom. Used from the
 * "new assignment" flow reached via a Client's own record, where the client side is fixed and the
 * EDJEr side needs picking.
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
