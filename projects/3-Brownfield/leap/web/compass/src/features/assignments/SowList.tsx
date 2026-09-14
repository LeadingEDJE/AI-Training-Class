import { Table, StatusPill } from '../../components/ui';
import { tableLinkClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';

/** One contract period row, per `contracts/sow-write-surface.md` §3. */
export interface SowListRow {
  id: number;
  /**
   * `LegacyMigrated` is reachable here even though this write surface refuses it on input
   * (FR-016) — a row seeded that way by the migration (Stream 6) reads back through this same
   * list. Editing one is the first-edit grandfathering exit, US8/#67, not this component.
   */
  sowType: 'InitialContract' | 'SowExtension' | 'LegacyMigrated';
  sowStartDate: string;
  sowEndDate: string;
  /** Elevated-visibility only; absent from the response entirely for a non-elevated viewer. */
  rateIncrease: boolean | null;
  /** Elevated-visibility only; absent from the response entirely for a non-elevated viewer. */
  note: string | null;
}

interface SowListProps {
  rows: SowListRow[];
  /**
   * Whether this viewer may see `RateIncrease`/`Note` — Compass Ops or Super Admin, per contract §1.
   * The server already withholds both fields entirely for anyone else (FR-018, BR-1); this mirrors
   * that decision on the client so the column never renders empty cells with nothing to show.
   */
  elevated: boolean;
  onEdit: (id: number) => void;
  /**
   * Whether this viewer may permanently delete a SOW (issue #593) — Compass Super Admin only, never
   * Ops. A USABILITY affordance only, matching every other permission check on this screen: the
   * server refuses the DELETE independently, so hiding the action for anyone else is not the
   * authorization boundary.
   */
  canDelete?: boolean;
  onDelete?: (id: number) => void;
}

const TYPE_LABELS: Record<SowListRow['sowType'], string> = {
  InitialContract: 'Initial Contract',
  SowExtension: 'SOW Extension',
  LegacyMigrated: 'Legacy (Migrated)',
};

/**
 * US3/#66 — the mockup screen 5 "SOWs / Contracts" table: `Type / SOW Start / SOW End / Rate
 * Increase / Note`, elevated-only columns omitted entirely for a non-elevated viewer.
 *
 * **A `LegacyMigrated` row is editable, like every other row** (US8/#67, issue #404). It used to
 * render with no Edit action, on the reasoning that the form could not represent the type — but that
 * made AC-41's "full validation on the first application edit" unreachable, because there was no
 * first edit, and left `HasPassedApplicationValidation` dead for exactly the rows it exists for.
 * `SowForm` now renders a legacy row's type as read-only and sends it back unchanged; the server
 * runs the full FR-019/FR-020 validation on that edit, which IS the exit from legacy state.
 */
export function SowList({ rows, elevated, onEdit, canDelete = false, onDelete }: SowListProps) {
  // Named, not blank: axe reports an empty `<th>` as `empty-table-header`.
  const columns = elevated
    ? ['Type', 'SOW Start', 'SOW End', 'Rate Increase', 'Note', 'Actions']
    : ['Type', 'SOW Start', 'SOW End', 'Actions'];

  return (
    <Table
      caption="Contract periods"
      columns={columns}
      emptyMessage="No SOWs or Contracts recorded yet."
    >
      {rows.length > 0 &&
        rows.map((row) => (
          <tr key={row.id}>
            <td className="px-3 py-2 text-sm">{TYPE_LABELS[row.sowType]}</td>
            <td className="px-3 py-2 text-sm">{formatDate(row.sowStartDate)}</td>
            <td className="px-3 py-2 text-sm">{formatDate(row.sowEndDate)}</td>
            {elevated && (
              <td className="px-3 py-2 text-sm">
                {row.rateIncrease === null ? (
                  '—'
                ) : (
                  <StatusPill
                    tone={row.rateIncrease ? 'affirmative' : 'neutral'}
                    label={row.rateIncrease ? 'Yes' : 'No'}
                  />
                )}
              </td>
            )}
            {elevated && <td className="px-3 py-2 text-sm">{row.note ?? ''}</td>}
            <td className="px-3 py-2 text-sm">
              {/* A `<button>`, not a link: it opens the inline editor rather than navigating. Only its
                  appearance is shared (`tableLinkClass`), so it reads as the same affordance as the
                  admin screens' Edit. `text-sm` matches this table's other cells. */}
              <button
                type="button"
                onClick={() => onEdit(row.id)}
                className={`${tableLinkClass} text-sm`}
              >
                Edit
              </button>
              {/* Issue #593 — Super Admin only, never Ops (the server refuses it independently).
                  `text-brand-danger` overrides `tableLinkClass`'s default text colour; it is already
                  measured against white (brand-contrast.test.ts) and this row's underlying surface IS
                  white, so no new contrast pairing is introduced. */}
              {canDelete && (
                <button
                  type="button"
                  onClick={() => onDelete?.(row.id)}
                  className={`${tableLinkClass} ml-3 text-sm text-brand-danger`}
                >
                  Delete
                </button>
              )}
            </td>
          </tr>
        ))}
    </Table>
  );
}
