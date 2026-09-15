import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createSkill,
  fetchSkillOptions,
  fetchSkills,
  skillOptionsQueryKey,
  skillsQueryKey,
  updateSkill,
} from '../../../../src/features/skills/skill-api';

const ADMIN_SKILLS = '/api/compass/v1/admin/skills';
const PUBLIC_SKILLS = '/api/compass/skills';

/**
 * The skill transport. Mirrors `lookup-api.test.ts` in shape — same discriminated-union outcomes —
 * plus {@link fetchSkillOptions}, the public read the Team Directory filter uses.
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

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('fetchSkills', () => {
  it('returns the collection on 200', async () => {
    stubResponse(200, [{ id: 1, name: 'Java', isActive: true }]);

    const result = await fetchSkills();

    expect(result).toEqual({ kind: 'loaded', values: [{ id: 1, name: 'Java', isActive: true }] });
  });

  it.each([401, 403])('reports a refusal on %i, not an empty list', async (status) => {
    stubResponse(status);

    expect(await fetchSkills()).toEqual({ kind: 'refused' });
  });

  it('reports a failure on 500, not an empty list', async () => {
    stubResponse(500);

    expect(await fetchSkills()).toEqual({ kind: 'failed' });
  });

  it('requests the admin route', async () => {
    const mock = stubResponse(200, []);

    await fetchSkills();

    expect(String(mock.mock.calls[0][0])).toContain(ADMIN_SKILLS);
  });
});

describe('createSkill', () => {
  it('reports saved on 201', async () => {
    stubResponse(201, { id: 9, name: 'Rust', isActive: true });

    expect(await createSkill('Rust')).toEqual({ kind: 'saved' });
  });

  it('posts only the name', async () => {
    const mock = stubResponse(201, {});

    await createSkill('Rust');

    const init = mock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ name: 'Rust' });
  });

  it("carries the server's message on a duplicate", async () => {
    stubResponse(409, { message: "A skill named 'Rust' already exists." });

    expect(await createSkill('Rust')).toEqual({
      kind: 'rejected',
      message: "A skill named 'Rust' already exists.",
    });
  });

  it('falls back to a generic message when a rejection carries no JSON body', async () => {
    stubResponse(409, undefined, true);

    expect(await createSkill('Rust')).toEqual({
      kind: 'rejected',
      message: 'The value could not be saved.',
    });
  });

  it('names permission as the reason on 403', async () => {
    stubResponse(403);

    expect(await createSkill('Rust')).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to change these values.',
    });
  });
});

describe('updateSkill', () => {
  it('reports saved on 200', async () => {
    stubResponse(200, { id: 1, name: 'Java', isActive: true });

    expect(await updateSkill(1, 'Java', true)).toEqual({ kind: 'saved' });
  });

  it('puts the name and the active flag together, addressed by id', async () => {
    const mock = stubResponse(200, {});

    await updateSkill(7, 'COBOL', false);

    const [url, init] = mock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toContain(`${ADMIN_SKILLS}/7`);
    expect(init.method).toBe('PUT');
    expect(JSON.parse(String(init.body))).toEqual({ name: 'COBOL', isActive: false });
  });

  it('reports a rejection with its message', async () => {
    stubResponse(404, { message: 'Not found.' });

    expect(await updateSkill(42, 'Nowhere', true)).toEqual({
      kind: 'rejected',
      message: 'Not found.',
    });
  });
});

describe('fetchSkillOptions', () => {
  it('returns the active-only collection on 200', async () => {
    stubResponse(200, [{ id: 1, name: 'Java' }]);

    expect(await fetchSkillOptions()).toEqual([{ id: 1, name: 'Java' }]);
  });

  it('requests the public route, not the admin one', async () => {
    const mock = stubResponse(200, []);

    await fetchSkillOptions();

    expect(String(mock.mock.calls[0][0])).toContain(PUBLIC_SKILLS);
    expect(String(mock.mock.calls[0][0])).not.toContain('/admin/');
  });

  it.each([401, 403, 500])('degrades to an empty list on %i, rather than throwing', async (status) => {
    stubResponse(status);

    expect(await fetchSkillOptions()).toEqual([]);
  });
});

describe('the query keys', () => {
  it('names the admin collection and the public options separately', () => {
    expect(skillsQueryKey()).toEqual(['compass', 'skills']);
    expect(skillOptionsQueryKey()).toEqual(['compass', 'skills', 'options']);
  });
});
