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
 * **Status is RENDERED here, never computed here** (US4, T101/T107). The value arrives already derived
 * from the client's assignments; a column this screen filled in by its own reckoning would be a second
 * implementation of the derivation, which FR-029 forbids outright — and the two would disagree on the
 * exactly-today boundary, which is the case AC-42 says independent implementations get wrong.
 *
 * Since issue #274 the column carries THREE words. `Former` (worked with, no current assignment) is
 * what this screen was asked for: it and `Inactive` (set up, never engaged) used to read the same, so
 * the list could not tell a finished engagement from one that never started.
 *
 * "Internal" is a different thing entirely: a stored flag on the record, not a derived state. The two
 * columns sit side by side and are easy to conflate, which is why each renders its own words rather
 * than a shared badge vocabulary.
 */
const COLUMNS = ['Client', 'Status', 'Internal', 'Invoice Frequency', 'Actions'];

/**
 * Client administration — find a record and open it (issue #60).
 *
 * Deliberately a minimal admin list, matching `EdjerListPage`'s scope decision: the read-only Client
 * Directory that feature 005 built is a separate screen with separate columns, and duplicating it here
 * would pre-empt that work. Absent for one specific reason:
 *
 * - **No assignment counts or current-EDJEr column.** Those need assignment reads, which are Stream 3's
 *   (spec A-6).
 *
 * **A status filter WAS deliberately absent, and is not any more (issue #640).** FR-036 forbids derived
 * status from gating **selection, editing or authorization** — concretely, the assignment picker (a
 * brand-new client is `Inactive`, so a status-gated picker could never assign one, and a `Former`
 * client could never be re-engaged). Narrowing what THIS list *displays* is neither: nothing here feeds
 * the picker, and `filterClients` runs entirely in the browser over data already loaded, the same shape
 * `EdjerListPage`'s own status filter (issue #406) already uses. Client Directory's own FR-030
 * anticipated exactly this for the read-only screen; this is the admin list catching up to it.
 *
 * Reaching this screen is not what authorises anything behind it. Every route requires the Compass root
 * policy server-side and a Compass Admin is refused even on the reads; the `admin-config` nav gate is a
 * convenience over that enforcement, never a substitute (FR-028, Principle IV).
 */
export function ClientListPage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<ClientStatusFilter>('all');

  const { data, isPending } = useQuery({
    queryKey: clientsQueryKey(),
    queryFn: fetchClients,
  });

  // Memoised so it is a stable reference — a fresh `[]` each render would change `visible`'s dependency
  // identity every time, defeating the memo below.
  const clients = useMemo(() => (data?.kind === 'loaded' ? data.value : []), [data]);

  // Filtered in the browser, not by re-querying: the whole collection is in hand at this volume, so a
  // request per keystroke would be slower AND would need debouncing.
  const visible = useMemo(() => filterClients(clients, search, status), [clients, search, status]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Clients"
        actions={
          // A link rather than a button: it navigates, so it belongs in the tab order as a link and
          // supports open-in-new-tab like any other.
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

      {/* `Alert` rather than the class run it was copied from — the same extraction feature 008 made
          for the EDJEr screens, applied to the two copies this one still held. */}
      {data?.kind === 'refused' && (
        <Alert>You do not have permission to manage client records.</Alert>
      )}

      {data?.kind === 'failed' && <Alert>Clients could not be loaded. Try again.</Alert>}

      {data?.kind === 'loaded' && (
        // The white sheet the design source puts a list on (owner request 2026-08-18).
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
                  : // A distinct message, because "the search found nothing" and "there are no
                    // clients" are different facts and one of them would be alarming if reported as
                    // the other. A blank search with no match is the status filter's doing, so it
                    // gets its own generic wording rather than quoting an empty string (issue #640).
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
 * Narrows by derived status, then matches the query against the client's name, case-insensitively
 * (issue #640). Status is applied first: it is the coarser filter, and works together with search
 * rather than replacing it — the same order `EdjerListPage.filterEdjers` uses for its own two controls.
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
        // type="search" gives the input the searchbox role, which is how both the unit tests and the
        // Playwright spec address it — and gives the browser its clear affordance for free.
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={`${fieldControlClass} max-w-sm`}
      />
    </div>
  );
}

/**
 * The status filter (issue #640) — All, Active, Inactive or Former, defaulting to All so the list
 * shows every client until the administrator narrows it. Mirrors `EdjerListPage`'s own status control
 * in shape, not in default: that one opens on Active because a departed EDJEr is the exception an
 * administrator usually wants hidden, whereas a client browsing this list has no such default to favour.
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
        {/* The WORD the server sent, not a colour and not a re-derivation (AC-NFR-5, FR-029). A
            coloured dot would convey nothing in greyscale and nothing to a screen reader, and
            recomputing it here would be the second implementation BR-11 forbids.

            THREE words since issue #274, and deliberately still two tones: `Inactive` and `Former`
            share the neutral colourway and are told apart by the label. That is AC-NFR-5 applied
            rather than a shortcut — a third tint would carry the distinction only for readers who see
            colour, while the word carries it for everyone. Note the test on the fixture that
            contradicts itself: this branch keys on 'Active' alone, so an unrecognised value still
            renders its own word rather than being silently relabelled. */}
        <StatusPill
          tone={client.status === 'Active' ? 'affirmative' : 'neutral'}
          label={client.status}
        />
      </td>
      <td className="px-3 py-2">
        {/* The word, not a colour (AC-NFR-5). "Internal" / "External" rather than a checkmark, so the
            cell means the same thing in greyscale and to a screen reader. */}
        <StatusPill
          tone={client.isInternal ? 'affirmative' : 'neutral'}
          label={client.isInternal ? 'Internal' : 'External'}
        />
      </td>
      <td className="px-3 py-2">
        {/* No default is an ordinary state (AC-22), not a gap to be flagged. */}
        {client.invoiceFrequencyTypeName ?? '—'}
      </td>
      <td className="px-3 py-2">
        {/* Visible text is just "Edit"; the accessible name carries the client, so a screen-reader user
            in a list of identical links knows which row they are on. WCAG 2.5.3 holds because the
            accessible name CONTAINS the visible label — extending one is fine, replacing it is not. */}
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
