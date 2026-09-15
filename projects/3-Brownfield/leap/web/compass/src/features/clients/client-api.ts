import { apiFetch, apiUrl } from '../../lib/api-url';

/**
 * A client's derived status — `Active`, `Inactive` or `Former` — declared locally rather than
 * re-exported, per issue #274, so a widened union elsewhere cannot leak into this module.
 */
export type { ClientStatus } from '../../lib/status';

import type { ClientStatus } from '../../lib/status';

/**
 * A client as the administration list renders them.
 *
 * `status` is stored directly on the record (FR-035) and is sent back unchanged through
 * {@link CompassClientRequest} on every save.
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
  msaSignedDate: string | null;
  ndaSignedDate: string | null;
  isInternal: boolean;
  invoiceFrequencyTypeId: number | null;
  billableTimeCategories: BillableTimeCategory[];
  status: ClientStatus;
}

export type CompassClientRequest = Omit<CompassClient, 'id' | 'billableTimeCategories' | 'status'>;

/**
 * The outcome of reading client data. A refusal collapses into the same empty-list rendering as a
 * failure, since the nav already hides these screens from a role that would see a 403.
 */
export type ClientLoad<T> = { kind: 'loaded'; value: T } | { kind: 'refused' } | { kind: 'failed' };

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
    // ignore
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

/** Adds a client, which becomes assignable only once it has at least one billable category (FR-022). */
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
