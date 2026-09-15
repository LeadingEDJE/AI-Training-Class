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

  // The status filter replaces whatever the server narrowed by search (issue #640) — only one of
  // the two conditions is ever applied to a given render.
  const visibleRows = useMemo(() => filterByStatus(rows, status), [rows, status]);

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-6 px-6 py-8 text-brand-text">
      <PageHeader title="Client Directory" />

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
 * The status filter (issue #640) — All, Active, Inactive or Former, defaulting to All. Its value is
 * sent to the server the same way `search` and `sort` are, via `onSearchChange`/`onSortChange`.
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
