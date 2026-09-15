import { apiFetch, apiUrl } from '../../lib/api-url';

export interface CompassEdjerSummary {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
  employeeTypeName: string;
  isActive: boolean;
  hireDate: string;
  stateOfResidence: string;
  /** The coach's resolved display name, rendered as a link to the coach's own record. */
  coachName: string | null;
}

export interface CompassEdjer {
  id: number;
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  employeeTypeId: number;
  coachEmployeeId: number | null;
  stateOfResidence: string;
  /**
   * The IANA identifier of one of the six US zones (FR-8.1) — `America/New_York`, not `Eastern`.
   *
   * Optional on a save: the form omits it whenever the field is left at its default, and the
   * server silently keeps whatever zone the record already had.
   */
  timezone: string;
  /**
   * Whether this EDJEr is on the delivery team (issue #502).
   *
   * One of the time-tracking flags below, so a lesser tier never receives it on read and a save
   * from that tier omits it entirely rather than sending a value the server would reject.
   */
  isDeliveryTeam: boolean;
  isActive: boolean;
  timesheetRequired: boolean;
  canSubmitUnder40: boolean;
  includeInPayroll: boolean;
}

export type CompassEdjerRequest = Omit<CompassEdjer, 'id'>;

export interface BlockingAssignment {
  assignmentId: number;
  clientId: number;
  clientName: string;
  startDate: string;
}

export type EdjerLoad<T> = { kind: 'loaded'; value: T } | { kind: 'refused' } | { kind: 'failed' };

/**
 * The outcome of a write.
 *
 * **Two arms, not three.** The `blocked` case was merged into `rejected` once the deactivation
 * guard's 422 started carrying the same shape as any other rejection — `message` alone is enough,
 * and the blocking assignments travel in that message text rather than as a separate field.
 */
export type EdjerWrite =
  | { kind: 'saved'; value: CompassEdjer }
  | { kind: 'rejected'; message: string }
  | { kind: 'blocked'; message: string; assignments: BlockingAssignment[] };

const ADMIN_ROOT = '/api/compass/v1/admin/edjers';

async function rejectionMessage(response: Response): Promise<string> {
  if (response.status === 403) {
    return 'You do not have permission to change EDJEr records.';
  }

  if (response.status === 404) {
    return 'That EDJEr no longer exists.';
  }

  try {
    const body = (await response.json()) as { message?: string } | null;
    if (body?.message) {
      return body.message;
    }
  } catch {}

  return 'The EDJEr could not be saved.';
}

async function readWrite(response: Response, expected: number): Promise<EdjerWrite> {
  if (response.status === expected) {
    return { kind: 'saved', value: (await response.json()) as CompassEdjer };
  }

  if (response.status === 422) {
    // The body is read separately for the message and for the assignments below, since a
    // Response's JSON body can be parsed more than once without issue.
    try {
      const body = (await response.json()) as {
        message?: string;
        blockingAssignments?: BlockingAssignment[];
      } | null;

      return {
        kind: 'blocked',
        message: body?.message ?? 'This EDJEr still holds assignments with no end date.',
        assignments: body?.blockingAssignments ?? [],
      };
    } catch {
      return {
        kind: 'blocked',
        message: 'This EDJEr still holds assignments with no end date.',
        assignments: [],
      };
    }
  }

  return { kind: 'rejected', message: await rejectionMessage(response) };
}

export async function fetchEdjers(): Promise<EdjerLoad<CompassEdjerSummary[]>> {
  const response = await apiFetch(apiUrl(ADMIN_ROOT));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as CompassEdjerSummary[] };
}

export async function fetchEdjer(id: number): Promise<EdjerLoad<CompassEdjer>> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as CompassEdjer };
}

export async function createEdjer(request: CompassEdjerRequest): Promise<EdjerWrite> {
  const response = await apiFetch(apiUrl(ADMIN_ROOT), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return readWrite(response, 201);
}

export async function updateEdjer(id: number, request: CompassEdjerRequest): Promise<EdjerWrite> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return readWrite(response, 200);
}

export function edjersQueryKey(): [string, string] {
  return ['compass', 'edjers'];
}

export function edjerQueryKey(id: number): [string, string, number] {
  return ['compass', 'edjers', id];
}
