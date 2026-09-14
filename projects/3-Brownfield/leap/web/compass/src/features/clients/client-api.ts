import { apiFetch, apiUrl } from '../../lib/api-url';

/**
 * A client's derived status, as the server publishes it — `Active`, `Inactive` or `Former`.
 *
 * Re-exported from `lib/status` rather than declared here: the same vocabulary appears on four
 * surfaces, and issue #274 found the cost of a per-feature copy — the client-view assignment row was
 * typed with the CLIENT union, so widening it would have silently admitted `Former` to a row the
 * server never sends it. See {@link ClientStatus} for the three values and why they are total.
 */
export type { ClientStatus } from '../../lib/status';

// Imported as well as re-exported: `export type { X } from` publishes the name but does NOT bind it in
// this module's scope, so the interfaces below could not refer to it. `tsc -b` catches that; a bare
// `tsc --noEmit -p tsconfig.json` does not, because the root config is solution-style.
import type { ClientStatus } from '../../lib/status';

/**
 * A client as the administration list renders them.
 *
 * `status` is DERIVED per request from the client's assignments and is never stored (FR-035). It is
 * reported, never sent: {@link CompassClientRequest} has no member for it and the server refuses a
 * request carrying one.
 */
export interface CompassClientSummary {
  id: number;
  clientName: string;
  isInternal: boolean;
  /** The cadence's display name, or null when the client has no default. Null is ordinary, not missing. */
  invoiceFrequencyTypeName: string | null;
  status: ClientStatus;
}

/** One client-scoped billable time category. */
export interface BillableTimeCategory {
  id: number;
  categoryName: string;
  isActive: boolean;
}

/** One client's full configuration record — AC-21's field set, plus its categories. */
export interface CompassClient {
  id: number;
  clientName: string;
  /** ISO `yyyy-MM-dd`, or null. A Postgres `date`, so there is no time component to lose. */
  msaSignedDate: string | null;
  ndaSignedDate: string | null;
  isInternal: boolean;
  invoiceFrequencyTypeId: number | null;
  billableTimeCategories: BillableTimeCategory[];
  status: ClientStatus;
}

/**
 * What a client save sends.
 *
 * **Derived from the record by omission, deliberately.** `id` is supplied by the route,
 * `billableTimeCategories` are written through their own routes — sending the collection back would let
 * a client edit silently rewrite categories it never intended to touch — and `status` is DERIVED, so
 * sending it back would be asking the server to store a value it computes. The server additionally REFUSES
 * any member it does not recognise (`JsonUnmappedMemberHandling.Disallow`), so an accidental extra field
 * here is a 400 rather than a silent no-op — which is exactly what makes the status prohibition real.
 */
export type CompassClientRequest = Omit<CompassClient, 'id' | 'billableTimeCategories' | 'status'>;

/**
 * The outcome of reading client data.
 *
 * A refusal and a failure are distinct states, and neither is an empty list: rendering "no clients yet"
 * for a 403 would tell a Super Admin their client list had vanished. The nav hides these screens from
 * lesser roles, but a deep link does not, so the refused state is reachable.
 */
export type ClientLoad<T> = { kind: 'loaded'; value: T } | { kind: 'refused' } | { kind: 'failed' };

/**
 * The outcome of a write.
 *
 * **Two arms, not three.** Unlike `EdjerWrite`, there is no `blocked`: the 422 that arm exists for is
 * the EDJEr deactivation guard, and a client has no deactivation to guard (FR-021). If a future stream
 * adds a precondition failure here, add the arm then rather than carrying an unreachable one now.
 */
export type ClientWrite<T> = { kind: 'saved'; value: T } | { kind: 'rejected'; message: string };

const ADMIN_ROOT = '/api/compass/v1/admin/clients';

/** The server's message for a rejected write, or a fallback if it sent none. */
async function rejectionMessage(response: Response, subject: string): Promise<string> {
  if (response.status === 403) {
    return 'You do not have permission to change client records.';
  }

  if (response.status === 404) {
    return `That ${subject} no longer exists.`;
  }

  try {
    const body = (await response.json()) as { message?: string } | null;
    if (body?.message) {
      return body.message;
    }
  } catch {
    // A rejection without a JSON body is still a rejection; fall through to the generic message.
  }

  return `The ${subject} could not be saved.`;
}

/** Reads the outcome of a POST or PUT. */
async function readWrite<T>(
  response: Response,
  expected: number,
  subject: string,
): Promise<ClientWrite<T>> {
  if (response.status === expected) {
    return { kind: 'saved', value: (await response.json()) as T };
  }

  return { kind: 'rejected', message: await rejectionMessage(response, subject) };
}

/** Reads the outcome of a collection or record GET. */
async function readLoad<T>(response: Response): Promise<ClientLoad<T>> {
  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    // A 404 is a failure to load rather than its own state: the screen reached for a record that is not
    // there, and there is nothing for the administrator to do about it but go back.
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as T };
}

/** Every client — what the administration list renders. */
export async function fetchClients(): Promise<ClientLoad<CompassClientSummary[]>> {
  return readLoad<CompassClientSummary[]>(await apiFetch(apiUrl(ADMIN_ROOT)));
}

/** One client's full record, for the form to edit. */
export async function fetchClient(id: number): Promise<ClientLoad<CompassClient>> {
  return readLoad<CompassClient>(await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`)));
}

/** Adds a client, immediately available for assignment even at zero assignments (FR-022). */
export async function createClient(
  request: CompassClientRequest,
): Promise<ClientWrite<CompassClient>> {
  const response = await apiFetch(apiUrl(ADMIN_ROOT), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return readWrite<CompassClient>(response, 201, 'client');
}

/** Updates a client's details, internal flag and invoice-frequency default. */
export async function updateClient(
  id: number,
  request: CompassClientRequest,
): Promise<ClientWrite<CompassClient>> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return readWrite<CompassClient>(response, 200, 'client');
}

/** Adds a billable time category. Names are unique PER CLIENT, so a 409 is scoped to this one. */
export async function addBillableTimeCategory(
  clientId: number,
  categoryName: string,
): Promise<ClientWrite<BillableTimeCategory>> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${clientId}/billable-time-categories`), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ categoryName }),
  });

  return readWrite<BillableTimeCategory>(response, 201, 'category');
}

/**
 * Renames a category and/or retires it.
 *
 * **This is the only retirement path — there is no delete** (AC-23, Principle VIII). Timesheet records
 * reference these, so a retired category keeps its row and its name.
 */
export async function updateBillableTimeCategory(
  clientId: number,
  categoryId: number,
  categoryName: string,
  isActive: boolean,
): Promise<ClientWrite<BillableTimeCategory>> {
  const response = await apiFetch(
    apiUrl(`${ADMIN_ROOT}/${clientId}/billable-time-categories/${categoryId}`),
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ categoryName, isActive }),
    },
  );

  return readWrite<BillableTimeCategory>(response, 200, 'category');
}

/** The react-query key for the client collection. */
export function clientsQueryKey(): [string, string] {
  return ['compass', 'admin-clients'];
}

/** The react-query key for one client's record. */
export function clientQueryKey(id: number): [string, string, number] {
  return ['compass', 'admin-clients', id];
}
