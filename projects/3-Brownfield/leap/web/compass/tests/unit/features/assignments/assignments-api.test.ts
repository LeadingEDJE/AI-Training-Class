import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ASSIGNMENTS_ROOT,
  createAssignment,
  createSow,
  deleteAssignment,
  deleteSow,
  fetchAssignment,
  fetchClientPickers,
  fetchEdjerPickers,
  fetchSows,
  updateAssignment,
  updateSow,
} from '../../../../src/features/assignments/assignments-api';

/**
 * The assignment write-surface transport, asserted directly — mirroring `lookup-api.test.ts`. This
 * covers the mapping from HTTP to outcome, including paths a screen test cannot easily reach: a
 * rejection with no JSON body, and a 401 as distinct from a 403.
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

const ROW = {
  id: 1,
  employeeId: 10,
  employeeName: 'Ada Lovelace',
  clientId: 100,
  clientName: 'Acme',
  startDate: '2026-01-01',
  endDate: null,
  isCurrent: true,
  // Issue #518 — the client's internal-EDJE flag now travels on the row.
  isInternal: false,
};

describe('fetchAssignment', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('returns the row on 200', async () => {
    stubResponse(200, ROW);

    const result = await fetchAssignment(1);

    expect(result).toEqual({ kind: 'loaded', value: ROW });
  });

  it('requests the assignment by id', async () => {
    const mock = stubResponse(200, ROW);

    await fetchAssignment(1);

    expect(String(mock.mock.calls[0][0])).toBe(`${ASSIGNMENTS_ROOT}/1`);
  });

  it('resolves a 404 to a loaded null value, not a failure', async () => {
    // A legitimate, expected answer for an id that does not exist — not an error.
    stubResponse(404);

    const result = await fetchAssignment(999);

    expect(result).toEqual({ kind: 'loaded', value: null });
  });

  it.each([401, 403])('reports a refusal on %i, not a loaded null', async (status) => {
    stubResponse(status);

    const result = await fetchAssignment(1);

    expect(result).toEqual({ kind: 'refused' });
  });

  it('reports a failure on 500, not a loaded null', async () => {
    stubResponse(500);

    const result = await fetchAssignment(1);

    expect(result).toEqual({ kind: 'failed' });
  });
});

describe('createAssignment', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reports saved with the created row on 201', async () => {
    stubResponse(201, ROW);

    const result = await createAssignment({
      employeeId: 10,
      clientId: 100,
      startDate: '2026-01-01',
      endDate: null,
      note: null,
    });

    expect(result).toEqual({ kind: 'saved', value: ROW });
  });

  it('posts the request body as JSON', async () => {
    const mock = stubResponse(201, ROW);
    const request = {
      employeeId: 10,
      clientId: 100,
      startDate: '2026-01-01',
      endDate: null,
      note: 'A note',
    };

    await createAssignment(request);

    const init = mock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual(request);
  });

  it("carries the server's message on a rejection (FR-008)", async () => {
    stubResponse(400, { message: 'The end date must be on or after the start date.' });

    const result = await createAssignment({
      employeeId: 10,
      clientId: 100,
      startDate: '2026-06-01',
      endDate: '2026-01-01',
      note: null,
    });

    expect(result).toEqual({
      kind: 'rejected',
      message: 'The end date must be on or after the start date.',
    });
  });

  it('falls back to a generic message when a rejection carries no JSON body', async () => {
    stubResponse(400, undefined, true);

    const result = await createAssignment({
      employeeId: 10,
      clientId: 100,
      startDate: '2026-01-01',
      endDate: null,
      note: null,
    });

    expect(result).toEqual({ kind: 'rejected', message: 'The assignment could not be saved.' });
  });

  it('falls back when the body parses but names no message', async () => {
    stubResponse(400, {});

    const result = await createAssignment({
      employeeId: 10,
      clientId: 100,
      startDate: '2026-01-01',
      endDate: null,
      note: null,
    });

    expect(result).toEqual({ kind: 'rejected', message: 'The assignment could not be saved.' });
  });

  it('names permission as the reason on 403', async () => {
    // Reachable by deep link: the nav hides this screen but does not gate the request.
    stubResponse(403);

    const result = await createAssignment({
      employeeId: 10,
      clientId: 100,
      startDate: '2026-01-01',
      endDate: null,
      note: null,
    });

    expect(result).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to change assignments.',
    });
  });
});

describe('updateAssignment', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reports saved with the updated row on 200', async () => {
    const updated = { ...ROW, note: 'Adjusted' };
    stubResponse(200, updated);

    const result = await updateAssignment(1, {
      startDate: '2026-01-01',
      endDate: null,
      note: 'Adjusted',
    });

    expect(result).toEqual({ kind: 'saved', value: updated });
  });

  it('puts to the assignment addressed by id', async () => {
    const mock = stubResponse(200, ROW);

    await updateAssignment(7, { startDate: '2026-01-01', endDate: null, note: null });

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toBe(`${ASSIGNMENTS_ROOT}/7`);
    expect(init.method).toBe('PUT');
  });

  it('reports a rejection with its message on not-found (FR-004)', async () => {
    stubResponse(404, undefined, true);

    const result = await updateAssignment(999, {
      startDate: '2026-01-01',
      endDate: null,
      note: null,
    });

    expect(result).toEqual({ kind: 'rejected', message: 'The assignment could not be saved.' });
  });
});

describe('deleteAssignment', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reports deleted on 204', async () => {
    stubResponse(204);

    const result = await deleteAssignment(1);

    expect(result).toEqual({ kind: 'deleted' });
  });

  it('deletes the assignment addressed by id', async () => {
    const mock = stubResponse(204);

    await deleteAssignment(7);

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toBe(`${ASSIGNMENTS_ROOT}/7`);
    expect(init.method).toBe('DELETE');
  });

  it('reports a rejection with its message on not-found', async () => {
    stubResponse(404, undefined, true);

    const result = await deleteAssignment(999);

    expect(result).toEqual({ kind: 'rejected', message: 'The assignment could not be deleted.' });
  });

  it('names permission as the reason on 403 — issue #593 restricts this to Compass Super Admin', async () => {
    stubResponse(403);

    const result = await deleteAssignment(1);

    expect(result).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to delete assignments.',
    });
  });
});

describe('deleteSow', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reports deleted on 204', async () => {
    stubResponse(204);

    const result = await deleteSow(3, 1);

    expect(result).toEqual({ kind: 'deleted' });
  });

  it('deletes the sow addressed by assignment id and sow id', async () => {
    const mock = stubResponse(204);

    await deleteSow(3, 1);

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toBe(`${ASSIGNMENTS_ROOT}/3/sows/1`);
    expect(init.method).toBe('DELETE');
  });

  it('reports a rejection with its message on not-found', async () => {
    stubResponse(404, undefined, true);

    const result = await deleteSow(3, 999);

    expect(result).toEqual({ kind: 'rejected', message: 'The SOW could not be deleted.' });
  });

  it('names permission as the reason on 403 — issue #593 restricts this to Compass Super Admin', async () => {
    stubResponse(403);

    const result = await deleteSow(3, 1);

    expect(result).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to delete SOWs.',
    });
  });
});

describe('fetchClientPickers', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const CLIENT_ROW = { id: 1, clientName: 'Brand New', derivedStatus: 'Inactive' };

  it('returns every client on 200, including one derived Inactive (the O6 named regression test)', async () => {
    stubResponse(200, [CLIENT_ROW]);

    const result = await fetchClientPickers();

    expect(result).toEqual({ kind: 'loaded', values: [CLIENT_ROW] });
  });

  it('requests the client pickers route', async () => {
    const mock = stubResponse(200, []);

    await fetchClientPickers();

    expect(String(mock.mock.calls[0][0])).toBe(`${ASSIGNMENTS_ROOT}/pickers/clients`);
  });

  it.each([401, 403])('reports a refusal on %i, not an empty list', async (status) => {
    stubResponse(status);

    const result = await fetchClientPickers();

    expect(result).toEqual({ kind: 'refused' });
  });

  it('reports a failure on 500, not an empty list', async () => {
    stubResponse(500);

    const result = await fetchClientPickers();

    expect(result).toEqual({ kind: 'failed' });
  });
});

describe('fetchEdjerPickers', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const EDJER_ROW = { id: 9, displayName: 'Maya Alvarez' };

  it('returns every active EDJEr on 200', async () => {
    stubResponse(200, [EDJER_ROW]);

    const result = await fetchEdjerPickers();

    expect(result).toEqual({ kind: 'loaded', values: [EDJER_ROW] });
  });

  it('requests the EDJEr pickers route', async () => {
    const mock = stubResponse(200, []);

    await fetchEdjerPickers();

    expect(String(mock.mock.calls[0][0])).toBe(`${ASSIGNMENTS_ROOT}/pickers/edjers`);
  });

  it.each([401, 403])('reports a refusal on %i, not an empty list', async (status) => {
    stubResponse(status);

    const result = await fetchEdjerPickers();

    expect(result).toEqual({ kind: 'refused' });
  });

  it('reports a failure on 500, not an empty list', async () => {
    stubResponse(500);

    const result = await fetchEdjerPickers();

    expect(result).toEqual({ kind: 'failed' });
  });
});

describe('fetchSows', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const SOW_ROW = {
    id: 1,
    sowType: 'InitialContract' as const,
    sowStartDate: '2024-04-01',
    sowEndDate: '2024-12-31',
    rateIncrease: false,
    note: 'Initial 9-month SOW',
  };

  it('returns every period for the assignment on 200', async () => {
    stubResponse(200, [SOW_ROW]);

    const result = await fetchSows(3);

    expect(result).toEqual({ kind: 'loaded', values: [SOW_ROW] });
  });

  it('requests the sows route nested under the assignment', async () => {
    const mock = stubResponse(200, []);

    await fetchSows(3);

    expect(String(mock.mock.calls[0][0])).toBe(`${ASSIGNMENTS_ROOT}/3/sows`);
  });

  it.each([401, 403])('reports a refusal on %i, not an empty list', async (status) => {
    stubResponse(status);

    const result = await fetchSows(3);

    expect(result).toEqual({ kind: 'refused' });
  });

  it('reports a failure on 500, not an empty list', async () => {
    stubResponse(500);

    const result = await fetchSows(3);

    expect(result).toEqual({ kind: 'failed' });
  });
});

describe('createSow', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const REQUEST = {
    sowType: 'InitialContract' as const,
    rateIncrease: false,
    sowStartDate: '2024-04-01',
    sowEndDate: '2024-12-31',
    note: null,
  };

  it('reports saved with the created row on 201', async () => {
    const created = { id: 1, ...REQUEST };
    stubResponse(201, created);

    const result = await createSow(3, REQUEST);

    expect(result).toEqual({ kind: 'saved', value: created });
  });

  it('posts to the sows route nested under the assignment', async () => {
    const mock = stubResponse(201, { id: 1, ...REQUEST });

    await createSow(3, REQUEST);

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toBe(`${ASSIGNMENTS_ROOT}/3/sows`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual(REQUEST);
  });

  it('carries the overlap rejection message (FR-019)', async () => {
    stubResponse(409, { message: 'Dates overlap an existing SOW for this assignment.' });

    const result = await createSow(3, REQUEST);

    expect(result).toEqual({
      kind: 'rejected',
      message: 'Dates overlap an existing SOW for this assignment.',
    });
  });

  it('falls back to a generic message when a rejection carries no JSON body', async () => {
    stubResponse(400, undefined, true);

    const result = await createSow(3, REQUEST);

    expect(result).toEqual({ kind: 'rejected', message: 'The SOW could not be saved.' });
  });

  it('names permission as the reason on 403', async () => {
    stubResponse(403);

    const result = await createSow(3, REQUEST);

    expect(result).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to change SOWs.',
    });
  });
});

describe('updateSow', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const REQUEST = {
    sowType: 'InitialContract' as const,
    rateIncrease: false,
    sowStartDate: '2024-04-01',
    sowEndDate: '2024-12-31',
    note: 'Edited',
  };

  it('reports saved with the updated row on 200', async () => {
    const updated = { id: 1, ...REQUEST };
    stubResponse(200, updated);

    const result = await updateSow(3, 1, REQUEST);

    expect(result).toEqual({ kind: 'saved', value: updated });
  });

  it('puts to the sow addressed by assignment id and sow id', async () => {
    const mock = stubResponse(200, { id: 1, ...REQUEST });

    await updateSow(3, 1, REQUEST);

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toBe(`${ASSIGNMENTS_ROOT}/3/sows/1`);
    expect(init.method).toBe('PUT');
  });

  it('reports a rejection with its message on not-found (FR-022)', async () => {
    stubResponse(404, undefined, true);

    const result = await updateSow(3, 999, REQUEST);

    expect(result).toEqual({ kind: 'rejected', message: 'The SOW could not be saved.' });
  });
});
