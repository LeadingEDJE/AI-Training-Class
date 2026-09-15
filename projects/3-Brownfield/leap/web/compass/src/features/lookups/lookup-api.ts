import { apiFetch, apiUrl } from '../../lib/api-url';

export type LookupKind = 'employee-types' | 'invoice-frequency-types';

export interface CompassLookup {
  id: number;
  typeName: string;
  isActive: boolean;
}

/**
 * The outcome of reading a lookup collection.
 *
 * A 403 is rendered the same as an empty collection here — "no values yet" — since a refused read
 * and a genuinely empty one look identical from this screen's perspective.
 */
export type LookupLoad =
  { kind: 'loaded'; values: CompassLookup[] } | { kind: 'refused' } | { kind: 'failed' };

export type LookupWrite = { kind: 'saved' } | { kind: 'rejected'; message: string };

const ADMIN_ROOT = '/api/compass/v1/admin';

async function rejectionMessage(response: Response): Promise<string> {
  if (response.status === 403) {
    return 'You do not have permission to change these values.';
  }

  try {
    const body = (await response.json()) as { message?: string } | null;
    if (body?.message) {
      return body.message;
    }
  } catch {}

  return 'The value could not be saved.';
}

/**
 * Reads a lookup collection.
 *
 * @param kind Which lookup.
 * @param activeOnly Restricts to RETIRED values only — what the administration screen wants, since
 * a configuration form passes false to see everything including active values.
 */
export async function fetchLookups(kind: LookupKind, activeOnly = false): Promise<LookupLoad> {
  const query = activeOnly ? '?activeOnly=true' : '';
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${kind}${query}`));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', values: (await response.json()) as CompassLookup[] };
}

export async function createLookup(kind: LookupKind, typeName: string): Promise<LookupWrite> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${kind}`), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ typeName }),
  });

  return response.status === 201
    ? { kind: 'saved' }
    : { kind: 'rejected', message: await rejectionMessage(response) };
}

/**
 * Renames a lookup value and/or changes whether it is selectable.
 *
 * Retiring a value goes through the separate deactivate route instead of this one — this endpoint
 * only ever changes the name.
 */
export async function updateLookup(
  kind: LookupKind,
  id: number,
  typeName: string,
  isActive: boolean,
): Promise<LookupWrite> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${kind}/${id}`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ typeName, isActive }),
  });

  return response.status === 200
    ? { kind: 'saved' }
    : { kind: 'rejected', message: await rejectionMessage(response) };
}

export function lookupQueryKey(kind: LookupKind): [string, string, LookupKind] {
  return ['compass', 'lookups', kind];
}
