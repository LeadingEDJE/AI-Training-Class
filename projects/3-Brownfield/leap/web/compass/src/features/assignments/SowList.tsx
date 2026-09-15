import { Table, StatusPill } from '../../components/ui';
import { tableLinkClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';

export interface SowListRow {
  id: number;
  /**
   * `LegacyMigrated` never reaches this list — this write surface refuses it on both input and
   * output (FR-016), so a row seeded by the migration (Stream 6) is filtered out before it renders
   * here. Editing one is out of scope for this component entirely.
   */
  sowType: 'InitialContract' | 'SowExtension' | 'LegacyMigrated';
  sowStartDate: string;
  sowEndDate: string;
  rateIncrease: boolean | null;
  note: string | null;
}

interface SowListProps {
  rows: SowListRow[];
  /**
   * Whether the server has already withheld `RateIncrease`/`Note` for this viewer. The client does
   * not re-check the permission itself — it trusts this flag, which is why the column can safely
   * render whatever `rows` contains without an additional guard.
   */
  elevated: boolean;
  onEdit: (id: number) => void;
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
 * **A `LegacyMigrated` row shows no Edit action** (US8/#67, issue #404): the form cannot represent
 * that type, so the row renders read-only until a future ticket adds a migration path for it.
 */
export function SowList({ rows, elevated, onEdit, canDelete = false, onDelete }: SowListProps) {
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
              <button
                type="button"
                onClick={() => onEdit(row.id)}
                className={`${tableLinkClass} text-sm`}
              >
                Edit
              </button>
              {/* Issue #593 — available to Ops as well as Super Admin; the client is the only place
                  this permission is checked, since the delete endpoint has no server-side guard. */}
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
