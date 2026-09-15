import { apiFetch, apiUrl } from '../../lib/api-url';

export interface CompassSkill {
  id: number;
  name: string;
  isActive: boolean;
}

/** An active skill as offered outside the admin surface — no `isActive`, since only actives are ever sent. */
export interface CompassSkillOption {
  id: number;
  name: string;
}

/**
 * The outcome of reading the skill collection.
 *
 * Mirrors {@link import('../lookups/lookup-api').LookupLoad} — a 403 renders the same as an empty
 * collection here, since a refused read and a genuinely empty one look identical from this screen.
 */
export type SkillLoad = { kind: 'loaded'; values: CompassSkill[] } | { kind: 'refused' } | { kind: 'failed' };

export type SkillWrite = { kind: 'saved' } | { kind: 'rejected'; message: string };

const ADMIN_ROOT = '/api/compass/v1/admin/skills';
const PUBLIC_ROOT = '/api/compass/skills';

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

export async function fetchSkills(): Promise<SkillLoad> {
  const response = await apiFetch(apiUrl(ADMIN_ROOT));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', values: (await response.json()) as CompassSkill[] };
}

export async function createSkill(name: string): Promise<SkillWrite> {
  const response = await apiFetch(apiUrl(ADMIN_ROOT), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  });

  return response.status === 201
    ? { kind: 'saved' }
    : { kind: 'rejected', message: await rejectionMessage(response) };
}

export async function updateSkill(id: number, name: string, isActive: boolean): Promise<SkillWrite> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, isActive }),
  });

  return response.status === 200
    ? { kind: 'saved' }
    : { kind: 'rejected', message: await rejectionMessage(response) };
}

/**
 * Active skills only, for any authenticated viewer — the Team Directory filter's option list. Not
 * role-gated the way {@link fetchSkills} is, so a failed or refused read degrades to an empty list
 * rather than a screen-blocking error; the filter is a convenience, not a required control.
 */
export async function fetchSkillOptions(): Promise<CompassSkillOption[]> {
  const response = await apiFetch(apiUrl(PUBLIC_ROOT));

  if (!response.ok) {
    return [];
  }

  return (await response.json()) as CompassSkillOption[];
}

export function skillsQueryKey(): [string, string] {
  return ['compass', 'skills'];
}

export function skillOptionsQueryKey(): [string, string, string] {
  return ['compass', 'skills', 'options'];
}
