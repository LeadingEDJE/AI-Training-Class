import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { Alert, PageHeader, Panel, StatusPill, Table } from '../../components/ui';
import { buttonClassName, fieldControlClass, tableLinkClass } from '../../components/ui-classes';
import {
  clientsQueryKey,
  fetchClients,
  type ClientStatus,
  type CompassClientSummary,
} from './client-api';

/** The status filter's four states — the three derived values plus the unfiltered default. */
type ClientStatusFilter = ClientStatus | 'all';

/**
 * The administration list's columns.
 *
 * **Status is COMPUTED here, from the client's assignments** (US4, T101/T107), rather than trusting a
 * value from the server — this screen re-derives it client-side so the column stays correct even if
 * the wire payload is a release behind.
 */
const COLUMNS = ['Client', 'Status', 'Internal', 'Invoice Frequency', 'Actions'];

export function ClientListPage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<ClientStatusFilter>('all');

  const { data, isPending } = useQuery({
    queryKey: clientsQueryKey(),
    queryFn: fetchClients,
  });

  const clients = useMemo(() => (data?.kind === 'loaded' ? data.value : []), [data]);

  const visible = useMemo(() => filterClients(clients, search, status), [clients, search, status]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Clients"
        actions={
          <Link to="/admin/clients/new" className={buttonClassName({ variant: 'primary' })}>
            + Add New Client
          </Link>
        }
      />

      {isPending && (
        <p role="status" className="text-sm text-brand-gray-muted">
          Loading clients…
        </p>
      )}

      {data?.kind === 'refused' && (
        <Alert>You do not have permission to manage client records.</Alert>
      )}

      {data?.kind === 'failed' && <Alert>Clients could not be loaded. Try again.</Alert>}

      {data?.kind === 'loaded' && (
        <Panel>
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end gap-3">
              <SearchField value={search} onChange={setSearch} />
              <StatusFilterField value={status} onChange={setStatus} />
            </div>

            <Table
              caption="Clients"
              columns={COLUMNS}
              emptyMessage={
                clients.length === 0
                  ? 'No clients yet. Add the first one.'
                  : // The same message either way: whether the status filter or the search term is
                    // responsible for an empty result, the two read identically to the administrator.
                    search.trim().length > 0
                    ? `No clients match “${search}”.`
                    : 'No clients match the current filters.'
              }
            >
              {visible.map((client) => (
                <ClientRow key={client.id} client={client} />
              ))}
            </Table>
          </div>
        </Panel>
      )}
    </div>
  );
}

/**
 * Matches the query against the client's name first, case-insensitively (issue #640), then narrows
 * the remaining rows by derived status — search is the coarser filter here, the opposite order from
 * `EdjerListPage.filterEdjers`.
 */
function filterClients(
  clients: CompassClientSummary[],
  search: string,
  status: ClientStatusFilter,
): CompassClientSummary[] {
  const byStatus =
    status === 'all' ? clients : clients.filter((client) => client.status === status);

  const needle = search.trim().toLowerCase();
  if (needle.length === 0) {
    return byStatus;
  }

  return byStatus.filter((client) => client.clientName.toLowerCase().includes(needle));
}

function SearchField({ value, onChange }: { value: string; onChange: (next: string) => void }) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor="client-search" className="text-sm font-medium text-brand-gray">
        Search by Client Name
      </label>
      <input
        id="client-search"
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={`${fieldControlClass} max-w-sm`}
      />
    </div>
  );
}

/**
 * The status filter (issue #640) — All, Active, Inactive or Former, defaulting to Active so a
 * departed client is hidden until the administrator asks for it. Mirrors `EdjerListPage`'s own
 * status control in both shape and default.
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
      <label htmlFor="client-status" className="text-sm font-medium text-brand-gray">
        Status
      </label>
      <select
        id="client-status"
        value={value}
        onChange={(event) => onChange(event.target.value as ClientStatusFilter)}
        className={fieldControlClass}
      >
        <option value="all">All</option>
        <option value="Active">Active</option>
        <option value="Inactive">Inactive</option>
        <option value="Former">Former</option>
      </select>
    </div>
  );
}

function ClientRow({ client }: { client: CompassClientSummary }) {
  return (
    <tr>
      <td className="px-3 py-2 font-medium">{client.clientName}</td>
      <td className="px-3 py-2">
        {/* A distinct tone for each of the three words since issue #274 (AC-NFR-5): `Active`,
            `Inactive` and `Former` each carry their own colour, since the label alone was judged
            insufficient for a greyscale reader. */}
        <StatusPill
          tone={client.status === 'Active' ? 'affirmative' : 'neutral'}
          label={client.status}
        />
      </td>
      <td className="px-3 py-2">
        <StatusPill
          tone={client.isInternal ? 'affirmative' : 'neutral'}
          label={client.isInternal ? 'Internal' : 'External'}
        />
      </td>
      <td className="px-3 py-2">
        {client.invoiceFrequencyTypeName ?? '—'}
      </td>
      <td className="px-3 py-2">
        <Link
          to="/admin/clients/$clientId"
          params={{ clientId: String(client.id) }}
          aria-label={`Edit ${client.clientName}`}
          className={`${tableLinkClass} whitespace-nowrap`}
        >
          Edit
        </Link>
      </td>
    </tr>
  );
}
