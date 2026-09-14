import { apiFetch, apiUrl } from '../../lib/api-url';

/** Which lookup a request targets. The value is also the route segment. */
export type LookupKind = 'employee-types' | 'invoice-frequency-types';

/** A lookup value as the configuration API returns it. */
export interface CompassLookup {
  id: number;
  typeName: string;
  isActive: boolean;
}

/**
 * The outcome of reading a lookup collection.
 *
 * A refusal and a failure are distinct states, and neither is an empty list: rendering "no values yet"
 * for a 403 would tell a Super Admin their reference data had vanished. The nav hides this screen from
 * lesser roles, but a deep link does not, so the refused state is reachable.
 */
export type LookupLoad =
  { kind: 'loaded'; values: CompassLookup[] } | { kind: 'refused' } | { kind: 'failed' };

/**
 * The outcome of a write. A rejection carries the server's message so the reason reaches the
 * administrator — the API answers 409 for a duplicate name and 400 for a malformed one, both with a
 * message naming the problem.
 */
export type LookupWrite = { kind: 'saved' } | { kind: 'rejected'; message: string };

const ADMIN_ROOT = '/api/compass/v1/admin';

/** The server's message for a rejected write, or a fallback if it sent none. */
async function rejectionMessage(response: Response): Promise<string> {
  if (response.status === 403) {
    return 'You do not have permission to change these values.';
  }

  try {
    const body = (await response.json()) as { message?: string } | null;
    if (body?.message) {
      return body.message;
    }
  } catch {
    // A rejection without a JSON body is still a rejection; fall through to the generic message.
  }

  return 'The value could not be saved.';
}

/**
 * Reads a lookup collection.
 *
 * @param kind Which lookup.
 * @param activeOnly Restricts to selectable values — what a configuration form wants. The
 * administration screen passes false, because a retired value that cannot be seen can never be
 * reinstated.
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

/** Adds a lookup value. New values are created selectable. */
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
 * One request covers both, matching the API: retiring is an edit, which is why there is no separate
 * deactivate route. A caller retiring a value passes the name UNCHANGED.
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

/** The react-query key for one lookup collection. */
export function lookupQueryKey(kind: LookupKind): [string, string, LookupKind] {
  return ['compass', 'lookups', kind];
}
