import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createLookup,
  fetchLookups,
  lookupQueryKey,
  updateLookup,
} from '../../../../src/features/lookups/lookup-api';

const EMPLOYEE_TYPES = '/api/compass/v1/admin/employee-types';

/**
 * The lookup transport, asserted directly.
 *
 * `LookupAdminPage.test.tsx` covers what the screen DOES with these outcomes; this covers the mapping
 * from HTTP to outcome, including the paths a screen test cannot easily reach — a rejection with no
 * JSON body, and a 401 as distinct from a 403.
 */
function stubResponse(status: number, body?: unknown, bodyThrows = false) {
  const mock = vi.fn().mockResolvedValue({
    status,
    ok: status >= 200 && status < 300,
    json: bodyThrows ? () => Promise.reject(new Error('not json')) : () => Promise.resolve(body),
  } as unknown as Response);
  vi.stubGlobal('fetch', mock);
  return mock;
}

describe('fetchLookups', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('returns the collection on 200', async () => {
    stubResponse(200, [{ id: 1, typeName: 'Full Time', isActive: true }]);

    const result = await fetchLookups('employee-types');

    expect(result).toEqual({
      kind: 'loaded',
      values: [{ id: 1, typeName: 'Full Time', isActive: true }],
    });
  });

  it('asks for every value by default, so a retired one can still be reinstated', async () => {
    const mock = stubResponse(200, []);

    await fetchLookups('employee-types');

    expect(String(mock.mock.calls[0][0])).not.toContain('activeOnly');
  });

  it('asks for active values only when requested — the shape a configuration form wants', async () => {
    const mock = stubResponse(200, []);

    await fetchLookups('employee-types', true);

    expect(String(mock.mock.calls[0][0])).toContain('activeOnly=true');
  });

  it('sends the session cookie', async () => {
    // Bare fetch would drop it cross-origin and skip the 401 re-login path.
    const mock = stubResponse(200, []);

    await fetchLookups('employee-types');

    expect((mock.mock.calls[0][1] as RequestInit | undefined)?.credentials).toBe('include');
  });

  it.each([401, 403])('reports a refusal on %i, not an empty list', async (status) => {
    stubResponse(status);

    const result = await fetchLookups('employee-types');

    expect(result).toEqual({ kind: 'refused' });
  });

  it('reports a failure on 500, not an empty list', async () => {
    stubResponse(500);

    const result = await fetchLookups('employee-types');

    expect(result).toEqual({ kind: 'failed' });
  });

  it('targets the requested lookup', async () => {
    const mock = stubResponse(200, []);

    await fetchLookups('invoice-frequency-types');

    expect(String(mock.mock.calls[0][0])).toContain('/invoice-frequency-types');
  });
});

describe('createLookup', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reports saved on 201', async () => {
    stubResponse(201, { id: 9, typeName: 'Contract', isActive: true });

    const result = await createLookup('employee-types', 'Contract');

    expect(result).toEqual({ kind: 'saved' });
  });

  it('posts only the name — a new value is created selectable', async () => {
    const mock = stubResponse(201, {});

    await createLookup('employee-types', 'Contract');

    const init = mock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ typeName: 'Contract' });
  });

  it("carries the server's message on a duplicate", async () => {
    stubResponse(409, { message: "A value named 'Contract' already exists." });

    const result = await createLookup('employee-types', 'Contract');

    expect(result).toEqual({
      kind: 'rejected',
      message: "A value named 'Contract' already exists.",
    });
  });

  it('falls back to a generic message when a rejection carries no JSON body', async () => {
    // A proxy or a 502 can reject with an empty or HTML body. Surfacing "undefined" to an
    // administrator would be worse than a plain sentence.
    stubResponse(409, undefined, true);

    const result = await createLookup('employee-types', 'Contract');

    expect(result).toEqual({ kind: 'rejected', message: 'The value could not be saved.' });
  });

  it('falls back when the body parses but names no message', async () => {
    stubResponse(400, {});

    const result = await createLookup('employee-types', 'Contract');

    expect(result).toEqual({ kind: 'rejected', message: 'The value could not be saved.' });
  });

  it('names permission as the reason on 403', async () => {
    // Reachable by deep link: the nav hides this screen but does not gate the request.
    stubResponse(403);

    const result = await createLookup('employee-types', 'Contract');

    expect(result).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to change these values.',
    });
  });
});

describe('updateLookup', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reports saved on 200', async () => {
    stubResponse(200, { id: 1, typeName: 'Salaried', isActive: true });

    const result = await updateLookup('employee-types', 1, 'Salaried', true);

    expect(result).toEqual({ kind: 'saved' });
  });

  it('puts the name and the active flag together, addressed by id', async () => {
    // One request covers rename and retire, matching the API — there is no separate deactivate route.
    const mock = stubResponse(200, {});

    await updateLookup('employee-types', 7, 'Contract', false);

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toContain(`${EMPLOYEE_TYPES}/7`);
    expect(init.method).toBe('PUT');
    expect(JSON.parse(String(init.body))).toEqual({ typeName: 'Contract', isActive: false });
  });

  it('reports a rejection with its message', async () => {
    stubResponse(404, { message: 'Not found.' });

    const result = await updateLookup('employee-types', 42, 'Nowhere', true);

    expect(result).toEqual({ kind: 'rejected', message: 'Not found.' });
  });
});

describe('lookupQueryKey', () => {
  it('scopes the cache per lookup, so refreshing one does not refetch the other', () => {
    expect(lookupQueryKey('employee-types')).not.toEqual(lookupQueryKey('invoice-frequency-types'));
    expect(lookupQueryKey('employee-types')).toEqual(['compass', 'lookups', 'employee-types']);
  });
});
