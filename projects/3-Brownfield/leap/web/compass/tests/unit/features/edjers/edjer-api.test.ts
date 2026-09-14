import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubFetchByUrl } from '../../support/fetch-by-url';
import {
  createEdjer,
  edjerQueryKey,
  edjersQueryKey,
  fetchEdjer,
  fetchEdjers,
  updateEdjer,
  type CompassEdjerRequest,
} from '../../../../src/features/edjers/edjer-api';

/**
 * The EDJEr transport: what each HTTP status means to the screens above it.
 *
 * Mirrors `lookup-api.test.ts` in shape, with one addition that carries most of the weight — the **422**
 * the deactivation guard answers (FR-019, AC-19). The lookup surface has no equivalent, so this is the
 * first place in Compass where a rejection is *actionable* rather than corrective, and the difference has
 * to survive the transport layer intact.
 */
const EDJERS = '/api/compass/v1/admin/edjers';

const REQUEST: CompassEdjerRequest = {
  firstName: 'Ada',
  lastName: 'Lovelace',
  hireDate: '2020-01-06',
  email: 'ada.lovelace@example.test',
  employeeTypeId: 1,
  coachEmployeeId: null,
  stateOfResidence: 'OH',
  timezone: 'America/New_York',
  isDeliveryTeam: true,
  isActive: true,
  timesheetRequired: true,
  canSubmitUnder40: false,
  includeInPayroll: true,
};

const SAVED = { id: 7, ...REQUEST };

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('fetchEdjers', () => {
  it('returns the collection on 200', async () => {
    const summary = {
      id: 7,
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada.lovelace@example.test',
      employeeTypeName: 'Full Time',
      isActive: true,
    };
    const stub = stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [summary] } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await fetchEdjers();

    expect(result).toEqual({ kind: 'loaded', value: [summary] });
  });

  it('returns an empty collection as LOADED, not as failed', async () => {
    // The distinction the screen renders differently: "no EDJErs yet" is a real state, and conflating it
    // with an error would tell an administrator something is broken when nothing is.
    const stub = stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [] } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await fetchEdjers();

    expect(result).toEqual({ kind: 'loaded', value: [] });
  });

  it.each([401, 403])('reports %i as REFUSED rather than as an empty list', async (status) => {
    // Rendering "no EDJErs yet" for a refusal would tell a Super Admin their directory had vanished. The
    // nav hides this screen from lesser roles, but a deep link does not, so this state is reachable.
    const stub = stubFetchByUrl({ [`GET ${EDJERS}`]: { status } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await fetchEdjers();

    expect(result).toEqual({ kind: 'refused' });
  });

  it.each([404, 500, 503])('reports %i as FAILED', async (status) => {
    const stub = stubFetchByUrl({ [`GET ${EDJERS}`]: { status } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await fetchEdjers();

    expect(result).toEqual({ kind: 'failed' });
  });
});

describe('fetchEdjer', () => {
  it('returns one record on 200', async () => {
    const stub = stubFetchByUrl({ [`GET ${EDJERS}/7`]: { status: 200, body: SAVED } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await fetchEdjer(7);

    expect(result).toEqual({ kind: 'loaded', value: SAVED });
  });

  it('requests the id it was given', async () => {
    const stub = stubFetchByUrl({ [`GET ${EDJERS}/42`]: { status: 200, body: SAVED } });
    vi.stubGlobal('fetch', stub.mock);

    await fetchEdjer(42);

    expect(stub.calls()).toEqual([`GET ${EDJERS}/42`]);
  });

  it('reports a 403 as refused', async () => {
    const stub = stubFetchByUrl({ [`GET ${EDJERS}/7`]: { status: 403 } });
    vi.stubGlobal('fetch', stub.mock);

    expect(await fetchEdjer(7)).toEqual({ kind: 'refused' });
  });

  it('reports a 404 as failed, because there is nothing the administrator can do', async () => {
    const stub = stubFetchByUrl({ [`GET ${EDJERS}/7`]: { status: 404 } });
    vi.stubGlobal('fetch', stub.mock);

    expect(await fetchEdjer(7)).toEqual({ kind: 'failed' });
  });
});

describe('createEdjer', () => {
  it('POSTs the request and returns the saved record on 201', async () => {
    const stub = stubFetchByUrl({ [`POST ${EDJERS}`]: { status: 201, body: SAVED } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await createEdjer(REQUEST);

    expect(result).toEqual({ kind: 'saved', value: SAVED });
    expect(stub.bodiesFor(`POST ${EDJERS}`)).toEqual([REQUEST]);
  });

  it('sends every field of the record, so a form cannot silently drop one', async () => {
    // The list below and REQUEST above must together be the whole of CompassEdjerRequest. Nothing
    // type-checks either: tsconfig.app.json includes only `src`, so a fixture missing a required
    // member compiles, and the assertion then pins whatever the fixture happens to hold rather than
    // what the transport declares. Add to both, or this gate quietly stops being one.
    const stub = stubFetchByUrl({ [`POST ${EDJERS}`]: { status: 201, body: SAVED } });
    vi.stubGlobal('fetch', stub.mock);

    await createEdjer(REQUEST);

    const [sent] = stub.bodiesFor(`POST ${EDJERS}`) as [Record<string, unknown>];
    expect(Object.keys(sent).sort()).toEqual(
      [
        'canSubmitUnder40',
        'coachEmployeeId',
        'email',
        'employeeTypeId',
        'firstName',
        'hireDate',
        'includeInPayroll',
        'isActive',
        'isDeliveryTeam',
        'lastName',
        'stateOfResidence',
        'timesheetRequired',
        'timezone',
      ].sort(),
    );
  });

  it('surfaces the server message on a 409 email collision', async () => {
    // BR-9. The message names the field, and the transport must not replace it with a generic one — the
    // reason for a rejection is the useful part.
    const stub = stubFetchByUrl({
      [`POST ${EDJERS}`]: {
        status: 409,
        body: { message: "The email address 'ada@example.test' is already used by another EDJEr." },
      },
    });
    vi.stubGlobal('fetch', stub.mock);

    const result = await createEdjer(REQUEST);

    expect(result.kind).toBe('rejected');
    expect(result).toMatchObject({ message: expect.stringContaining('already used') });
  });

  it('surfaces the server message on a 400', async () => {
    const stub = stubFetchByUrl({
      [`POST ${EDJERS}`]: {
        status: 400,
        body: { message: 'A state of residence must be one of the 50 US states or DC.' },
      },
    });
    vi.stubGlobal('fetch', stub.mock);

    expect(await createEdjer(REQUEST)).toEqual({
      kind: 'rejected',
      message: 'A state of residence must be one of the 50 US states or DC.',
    });
  });

  it('says something useful on a 403, rather than echoing a permission error nobody wrote', async () => {
    const stub = stubFetchByUrl({ [`POST ${EDJERS}`]: { status: 403 } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await createEdjer(REQUEST);

    expect(result.kind).toBe('rejected');
    expect(result).toMatchObject({ message: expect.stringMatching(/permission/i) });
  });

  it('falls back to a generic message when a rejection carries no JSON body', async () => {
    // A rejection without a body is still a rejection. Throwing here would turn a 500 into a blank screen.
    const stub = stubFetchByUrl({ [`POST ${EDJERS}`]: { status: 500 } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await createEdjer(REQUEST);

    expect(result.kind).toBe('rejected');
    expect(result).toMatchObject({ message: expect.stringMatching(/could not be saved/i) });
  });
});

describe('updateEdjer', () => {
  it('PUTs to the id and returns the saved record on 200', async () => {
    const stub = stubFetchByUrl({ [`PUT ${EDJERS}/7`]: { status: 200, body: SAVED } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await updateEdjer(7, REQUEST);

    expect(result).toEqual({ kind: 'saved', value: SAVED });
    expect(stub.bodiesFor(`PUT ${EDJERS}/7`)).toEqual([REQUEST]);
  });

  it('reports a 404 as rejected, naming the record rather than the transport', async () => {
    const stub = stubFetchByUrl({ [`PUT ${EDJERS}/7`]: { status: 404 } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await updateEdjer(7, REQUEST);

    expect(result.kind).toBe('rejected');
    expect(result).toMatchObject({ message: expect.stringMatching(/no longer exists/i) });
  });

  it('reports a 409 as rejected', async () => {
    const stub = stubFetchByUrl({
      [`PUT ${EDJERS}/7`]: { status: 409, body: { message: 'Already used.' } },
    });
    vi.stubGlobal('fetch', stub.mock);

    expect(await updateEdjer(7, REQUEST)).toEqual({ kind: 'rejected', message: 'Already used.' });
  });

  // ------------------------------------------------------- the 422 the deactivation guard answers

  it('reports a 422 as BLOCKED, distinct from rejected, carrying the assignments', async () => {
    // The whole reason this union has three arms. A 422 means the request was well formed and authorised
    // and the world forbids it — and the response says what to change. Collapsing it into `rejected` would
    // lose the assignments, which are the only actionable part.
    const assignments = [
      { assignmentId: 42, clientId: 7, clientName: 'Buckeye Mutual', startDate: '2024-04-01' },
      { assignmentId: 43, clientId: 9, clientName: 'Olentangy Health', startDate: '2024-06-12' },
    ];
    const stub = stubFetchByUrl({
      [`PUT ${EDJERS}/7`]: {
        status: 422,
        body: {
          message: 'Ada Lovelace still holds 2 assignment(s) with no end date.',
          blockingAssignments: assignments,
        },
      },
    });
    vi.stubGlobal('fetch', stub.mock);

    const result = await updateEdjer(7, { ...REQUEST, isActive: false });

    expect(result).toEqual({
      kind: 'blocked',
      message: 'Ada Lovelace still holds 2 assignment(s) with no end date.',
      assignments,
    });
  });

  it('still reports BLOCKED when a 422 arrives with no assignment list', async () => {
    // Defensive, and the reason matters: the screen branches on `kind`, so a 422 that fell through to
    // `rejected` because its body was thin would render the wrong message entirely — a corrective one,
    // for a situation nothing about the request can correct.
    const stub = stubFetchByUrl({ [`PUT ${EDJERS}/7`]: { status: 422, body: {} } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await updateEdjer(7, { ...REQUEST, isActive: false });

    expect(result.kind).toBe('blocked');
    expect(result).toMatchObject({ assignments: [] });
  });

  it('still reports BLOCKED when a 422 carries no JSON at all', async () => {
    const stub = stubFetchByUrl({ [`PUT ${EDJERS}/7`]: { status: 422 } });
    vi.stubGlobal('fetch', stub.mock);

    const result = await updateEdjer(7, { ...REQUEST, isActive: false });

    expect(result.kind).toBe('blocked');
    expect(result).toMatchObject({ assignments: [] });
  });

  it('still reports BLOCKED when the 422 body cannot be parsed as JSON', async () => {
    // A distinct case from the one above, which resolves to `undefined`. This is a body that THROWS on
    // parse — an HTML error page from a proxy, say. Both must stay `blocked`, or a 422 would render the
    // corrective message for a situation nothing about the request can correct.
    //
    // Stubbed directly rather than through stubFetchByUrl, whose `json()` resolves and therefore cannot
    // reach this path at all.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        status: 422,
        ok: false,
        json: () => Promise.reject(new SyntaxError('Unexpected token < in JSON')),
      } as unknown as Response),
    );

    const result = await updateEdjer(7, { ...REQUEST, isActive: false });

    expect(result.kind).toBe('blocked');
    expect(result).toMatchObject({ assignments: [] });
  });

  it('falls back to a generic message when a REJECTED body cannot be parsed either', async () => {
    // The same hazard on the 400/409 path, which has its own try/catch.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        status: 400,
        ok: false,
        json: () => Promise.reject(new SyntaxError('Unexpected token < in JSON')),
      } as unknown as Response),
    );

    const result = await updateEdjer(7, REQUEST);

    expect(result.kind).toBe('rejected');
    expect(result).toMatchObject({ message: expect.stringMatching(/could not be saved/i) });
  });
});

describe('the query keys', () => {
  it('names the collection', () => {
    expect(edjersQueryKey()).toEqual(['compass', 'edjers']);
  });

  it('names one record under the collection, so invalidating the list reaches it', () => {
    // The record key is PREFIXED by the collection key, which is what makes
    // `invalidateQueries({ queryKey: edjersQueryKey() })` after a save also refresh the open form.
    expect(edjerQueryKey(7)).toEqual(['compass', 'edjers', 7]);
    expect(edjerQueryKey(7).slice(0, 2)).toEqual(edjersQueryKey());
  });

  it('distinguishes two records', () => {
    expect(edjerQueryKey(7)).not.toEqual(edjerQueryKey(8));
  });
});
