import { useMemo, useState } from 'react';
import { Alert, PageHeader, Panel, Table, type TableColumn } from '../../components/ui';
import { CONTROL_BORDER, tableLinkClass } from '../../components/ui-classes';
import type { ClientStatus } from '../../lib/status';
import type { ClientDirectoryRow } from './types';

/** The status filter's four states — the three derived values plus the unfiltered default. */
type ClientStatusFilter = ClientStatus | 'all';

interface ClientDirectoryPageProps {
  rows: ClientDirectoryRow[];
  isPending: boolean;
  isError: boolean;
  /** Which column the server is ordering by, if any. */
  sort?: string;
  descending?: boolean;
  onSearchChange?: (search: string) => void;
  onSortChange?: (column: string) => void;
}

/** The columns AC-12 allows sorting by. */
const COLUMNS = [
  { key: 'clientName', label: 'Client' },
  { key: 'status', label: 'Status' },
] as const;

/** The column the listing orders by when none is asked for. */
const DEFAULT_SORT_COLUMN = 'clientName';

/**
 * The Client Directory (AC-12).
 *
 * Two columns and a link, deliberately. MSA/NDA dates, the internal flag and the invoice default are
 * absent because AC-14 hides them from four of the five tiers on the client view — surfacing them in
 * a listing open to every authenticated viewer would defeat that.
 *
 * **Composed from `components/ui` since feature 008 (FR-020).** It previously hand-rolled every
 * element and imported none of them, which cost more than duplication: its `h1` was `text-2xl`
 * against `PageHeader`'s `text-xl`, so two title sizes shipped in one product; and its table set no
 * `aria-sort`, so a screen-reader user could activate a sort control and get no confirmation that
 * anything had been ordered.
 *
 * **The status filter (issue #640) is purely local, unlike search and sort.** Those two travel to the
 * server because AC-12's collection can be searched or ordered at the database; status is already
 * fully resolved on every row the server sends, so narrowing it needs nothing but a client-side
 * filter over data already in hand — the same reasoning `EdjerListPage`'s own status filter uses. It
 * defaults to All rather than Active: FR-030 asked only that status be "sortable and filterable",
 * with no default scope the way Team Directory's own status control has one.
 */
export function ClientDirectoryPage({
  rows,
  isPending,
  isError,
  sort,
  descending = false,
  onSearchChange,
  onSortChange,
}: ClientDirectoryPageProps) {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<ClientStatusFilter>('all');

  const activeSort = sort === undefined || sort === '' ? DEFAULT_SORT_COLUMN : sort;
  const direction = descending ? 'descending' : 'ascending';

  const columns: TableColumn[] = COLUMNS.map((column) => ({
    label: column.label,
    sortDirection: column.key === activeSort ? direction : undefined,
    onSort: () => onSortChange?.(column.key),
  }));

  // Filtered in the browser, on top of whatever the server already narrowed by search — the two work
  // together rather than one replacing the other (issue #640).
  const visibleRows = useMemo(() => filterByStatus(rows, status), [rows, status]);

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-6 px-6 py-8 text-brand-text">
      <PageHeader title="Client Directory" />

      {/* The white sheet the design source puts a listing on. The search control lives INSIDE it with
          the table, as on the Team Directory — the two belong to the same surface, and a search box
          floating on the shell above a sheeted table reads as two unrelated things (owner request
          2026-08-18). */}
      <Panel>
        <div className="flex flex-col gap-5">
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1">
              <label
                htmlFor="client-directory-search"
                className="text-sm font-medium text-brand-gray"
              >
                Search by Client Name
              </label>
              <input
                id="client-directory-search"
                type="search"
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  onSearchChange?.(event.target.value);
                }}
                // CONTROL_BORDER, not the taupe tint this shipped with. `ui-classes.ts` records that
                // taupe deliberately FAILS §1.4.11's 3:1 because it is meant for decorative separators
                // — a card edge is exempt, an input edge is not.
                className={`max-w-sm rounded border ${CONTROL_BORDER} px-3 py-2 text-sm`}
              />
            </div>

            <StatusFilterField value={status} onChange={setStatus} />
          </div>

          {isPending && (
            <p role="status" className="text-sm">
              Loading the client directory…
            </p>
          )}

          {isError && <Alert>The client directory could not be loaded.</Alert>}

          {!isPending && !isError && (
            <Table
              caption="Client Directory"
              columns={columns}
              emptyMessage="No clients match the current search or filters."
            >
              {visibleRows.map((row) => (
                <tr key={row.id} className="border-b border-brand-taupe/40">
                  <td className="px-3 py-2">
                    <a href={`/compass/client-directory/${row.id}`} className={tableLinkClass}>
                      {row.clientName}
                    </a>
                  </td>
                  {/* Status is conveyed as TEXT, never by colour alone — it has to survive a
                          greyscale render and a screen reader (AC-NFR-5). */}
                  <td className="px-3 py-2">{row.status}</td>
                </tr>
              ))}
            </Table>
          )}
        </div>
      </Panel>
    </main>
  );
}

/** Narrows the already-loaded rows to one derived status, or returns them unchanged for `'all'`. */
function filterByStatus(
  rows: ClientDirectoryRow[],
  status: ClientStatusFilter,
): ClientDirectoryRow[] {
  return status === 'all' ? rows : rows.filter((row) => row.status === status);
}

/**
 * The status filter (issue #640) — All, Active, Inactive or Former, defaulting to All. Purely local
 * state: see the module doc comment for why this control never reaches the server.
 */
function StatusFilterField({
  value,
  onChange,
}: {
  value: ClientStatusFilter;
  onChange: (next: ClientStatusFilter) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor="client-directory-status" className="text-sm font-medium text-brand-gray">
        Status
      </label>
      <select
        id="client-directory-status"
        value={value}
        onChange={(event) => onChange(event.target.value as ClientStatusFilter)}
        className={`rounded border ${CONTROL_BORDER} px-3 py-2 text-sm`}
      >
        <option value="all">All</option>
        <option value="Active">Active</option>
        <option value="Inactive">Inactive</option>
        <option value="Former">Former</option>
      </select>
    </div>
  );
}
