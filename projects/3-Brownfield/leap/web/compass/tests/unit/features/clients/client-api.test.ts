import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubFetchByUrl } from '../../support/fetch-by-url';
import {
  addBillableTimeCategory,
  clientQueryKey,
  clientsQueryKey,
  createClient,
  fetchClient,
  fetchClients,
  updateBillableTimeCategory,
  updateClient,
  type CompassClientRequest,
} from '../../../../src/features/clients/client-api';

/**
 * The client transport: what each HTTP status means to the screens above it.
 *
 * Mirrors `edjer-api.test.ts` in shape, minus the 422 — a client has no deactivation to guard, so that
 * arm would be unreachable here (FR-021). The addition is the per-client 409 on categories, which means
 * something narrower than the client-name 409: "this client already offers that", not "that name is
 * taken".
 */
const CLIENTS = '/api/compass/v1/admin/clients';

const REQUEST: CompassClientRequest = {
  clientName: 'Contoso',
  msaSignedDate: '2024-03-01',
  ndaSignedDate: null,
  isInternal: false,
  invoiceFrequencyTypeId: 1,
};

const SAVED = { id: 7, ...REQUEST, billableTimeCategories: [] };

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('fetchClients', () => {
  it('returns the collection on 200', async () => {
    stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: [SAVED] } });

    const result = await fetchClients();

    expect(result).toEqual({ kind: 'loaded', value: [SAVED] });
  });

  it.each([401, 403])('reports %i as a refusal, not an empty collection', async (status) => {
    stubFetchByUrl({ [`GET ${CLIENTS}`]: { status } });

    expect(await fetchClients()).toEqual({ kind: 'refused' });
  });

  it('reports any other non-200 as a failure', async () => {
    stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 500 } });

    expect(await fetchClients()).toEqual({ kind: 'failed' });
  });
});

describe('fetchClient', () => {
  it('returns the record on 200', async () => {
    stubFetchByUrl({ [`GET ${CLIENTS}/7`]: { status: 200, body: SAVED } });

    expect(await fetchClient(7)).toEqual({ kind: 'loaded', value: SAVED });
  });

  it('reports a 403 as a refusal', async () => {
    stubFetchByUrl({ [`GET ${CLIENTS}/7`]: { status: 403 } });

    expect(await fetchClient(7)).toEqual({ kind: 'refused' });
  });

  it('reports a 404 as a failure to load, not its own state', async () => {
    // There is nothing for the administrator to do about a record that is not there but go back, so it
    // does not earn a third arm.
    stubFetchByUrl({ [`GET ${CLIENTS}/7`]: { status: 404 } });

    expect(await fetchClient(7)).toEqual({ kind: 'failed' });
  });
});

describe('createClient', () => {
  it('sends the request and returns the saved record on 201', async () => {
    const stub = stubFetchByUrl({ [`POST ${CLIENTS}`]: { status: 201, body: SAVED } });

    const result = await createClient(REQUEST);

    expect(result).toEqual({ kind: 'saved', value: SAVED });
    expect(stub.bodiesFor(`POST ${CLIENTS}`)[0]).toEqual(REQUEST);
  });

  it('carries the server message on a 409 so the reason reaches the administrator', async () => {
    stubFetchByUrl({
      [`POST ${CLIENTS}`]: {
        status: 409,
        body: { message: "The client name 'X' is already in use." },
      },
    });

    expect(await createClient(REQUEST)).toEqual({
      kind: 'rejected',
      message: "The client name 'X' is already in use.",
    });
  });

  it('carries the server message on a 400', async () => {
    stubFetchByUrl({
      [`POST ${CLIENTS}`]: { status: 400, body: { message: 'That invoice frequency is retired.' } },
    });

    expect(await createClient(REQUEST)).toEqual({
      kind: 'rejected',
      message: 'That invoice frequency is retired.',
    });
  });

  it('states the permission problem itself on a 403, rather than echoing the server', async () => {
    stubFetchByUrl({ [`POST ${CLIENTS}`]: { status: 403 } });

    const result = await createClient(REQUEST);

    expect(result).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to change client records.',
    });
  });

  it('falls back to a generic message when a rejection carries no JSON body', async () => {
    stubFetchByUrl({ [`POST ${CLIENTS}`]: { status: 400, body: undefined } });

    const result = await createClient(REQUEST);

    expect(result.kind).toBe('rejected');
    expect((result as { message: string }).message).toMatch(/could not be saved/i);
  });
});

describe('updateClient', () => {
  it('returns the saved record on 200', async () => {
    const stub = stubFetchByUrl({ [`PUT ${CLIENTS}/7`]: { status: 200, body: SAVED } });

    expect(await updateClient(7, REQUEST)).toEqual({ kind: 'saved', value: SAVED });
    expect(stub.bodiesFor(`PUT ${CLIENTS}/7`)[0]).toEqual(REQUEST);
  });

  it('names the record as gone on a 404', async () => {
    stubFetchByUrl({ [`PUT ${CLIENTS}/7`]: { status: 404 } });

    expect(await updateClient(7, REQUEST)).toEqual({
      kind: 'rejected',
      message: 'That client no longer exists.',
    });
  });
});

describe('addBillableTimeCategory', () => {
  it('posts under the client and returns the category on 201', async () => {
    const category = { id: 11, categoryName: 'Development', isActive: true };
    const stub = stubFetchByUrl({
      [`POST ${CLIENTS}/7/billable-time-categories`]: { status: 201, body: category },
    });

    expect(await addBillableTimeCategory(7, 'Development')).toEqual({
      kind: 'saved',
      value: category,
    });
    expect(stub.bodiesFor(`POST ${CLIENTS}/7/billable-time-categories`)[0]).toEqual({
      categoryName: 'Development',
    });
  });

  it('carries the per-client duplicate message on a 409', async () => {
    stubFetchByUrl({
      [`POST ${CLIENTS}/7/billable-time-categories`]: {
        status: 409,
        body: { message: "This client already offers a category named 'Development'." },
      },
    });

    expect(await addBillableTimeCategory(7, 'Development')).toEqual({
      kind: 'rejected',
      message: "This client already offers a category named 'Development'.",
    });
  });

  it('names the CATEGORY, not the client, in its fallback message', async () => {
    // The subject matters: "the client could not be saved" after a failed category add would send the
    // administrator to look at the wrong thing.
    stubFetchByUrl({ [`POST ${CLIENTS}/7/billable-time-categories`]: { status: 400 } });

    const result = await addBillableTimeCategory(7, 'Development');

    expect((result as { message: string }).message).toMatch(/category could not be saved/i);
  });
});

describe('updateBillableTimeCategory', () => {
  it('retires a category through a PUT, never a DELETE', async () => {
    // AC-23 / Principle VIII: timesheet records reference these, so retirement is the flag going off.
    const stub = stubFetchByUrl({
      [`PUT ${CLIENTS}/7/billable-time-categories/11`]: {
        status: 200,
        body: { id: 11, categoryName: 'Development', isActive: false },
      },
    });

    const result = await updateBillableTimeCategory(7, 11, 'Development', false);

    expect(result.kind).toBe('saved');
    expect(stub.bodiesFor(`PUT ${CLIENTS}/7/billable-time-categories/11`)[0]).toEqual({
      categoryName: 'Development',
      isActive: false,
    });
    expect(stub.calls().some((call) => call.startsWith('DELETE'))).toBe(false);
  });

  it('names the category as gone on a 404, which is also what a wrong client id answers', async () => {
    // The client id is part of the server's lookup, so a category belonging to someone else is not
    // found rather than editable.
    stubFetchByUrl({ [`PUT ${CLIENTS}/7/billable-time-categories/11`]: { status: 404 } });

    expect(await updateBillableTimeCategory(7, 11, 'Development', true)).toEqual({
      kind: 'rejected',
      message: 'That category no longer exists.',
    });
  });
});

describe('query keys', () => {
  it('scopes the collection and the record separately', () => {
    expect(clientsQueryKey()).toEqual(['compass', 'admin-clients']);
    expect(clientQueryKey(7)).toEqual(['compass', 'admin-clients', 7]);
  });
});
