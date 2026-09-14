import { apiFetch, apiUrl } from '../../lib/api-url';

/** An EDJEr as the administration list renders them. */
export interface CompassEdjerSummary {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
  employeeTypeName: string;
  isActive: boolean;
  /** ISO `yyyy-MM-dd`. A Postgres `date`, so there is no time component to lose. */
  hireDate: string;
  stateOfResidence: string;
  /** The coach's resolved display name, or null when the EDJEr has none. Never a link on this screen. */
  coachName: string | null;
}

/** One EDJEr's full configuration record — exactly AC-17's field set. */
export interface CompassEdjer {
  id: number;
  firstName: string;
  lastName: string;
  /** ISO `yyyy-MM-dd`. A Postgres `date`, so there is no time component to lose. */
  hireDate: string;
  email: string;
  employeeTypeId: number;
  coachEmployeeId: number | null;
  stateOfResidence: string;
  /**
   * The IANA identifier of one of the six US zones (FR-8.1) — `America/New_York`, not `Eastern`.
   *
   * Always present on a record the server returns; the column is required and defaults to Eastern.
   * It is `CompassEdjerRequest`'s one field the server treats as optional, so that an EDJEr created
   * before FR-8.1 by a client that knew nothing about it is not a contract break — see
   * `CompassEdjerRequest.Timezone`. The form always sends it.
   */
  timezone: string;
  /**
   * Whether this EDJEr is on the delivery team (issue #502).
   *
   * Required on a record the server returns, and required on a save: the server treats it as
   * optional so an older client is not a contract break, but this form always sends it. Not one of
   * the time-tracking flags below, which a lesser tier never receives — this one is published to
   * every tier.
   */
  isDeliveryTeam: boolean;
  isActive: boolean;
  timesheetRequired: boolean;
  canSubmitUnder40: boolean;
  includeInPayroll: boolean;
}

/** What a save sends. Identical to the record minus the id, which the route supplies. */
export type CompassEdjerRequest = Omit<CompassEdjer, 'id'>;

/** An assignment standing in the way of a deactivation (AC-19). */
export interface BlockingAssignment {
  assignmentId: number;
  clientId: number;
  clientName: string;
  startDate: string;
}

/**
 * The outcome of reading EDJEr data.
 *
 * A refusal and a failure are distinct states, and neither is an empty list: rendering "no EDJErs yet"
 * for a 403 would tell a Super Admin their directory had vanished. The nav hides these screens from
 * lesser roles, but a deep link does not, so the refused state is reachable.
 */
export type EdjerLoad<T> = { kind: 'loaded'; value: T } | { kind: 'refused' } | { kind: 'failed' };

/**
 * The outcome of a write.
 *
 * **Three arms, not two.** `blocked` is the 422 the deactivation guard answers: the request was well
 * formed and authorised, and the state of the world forbids it (FR-019, AC-19). It carries the blocking
 * assignments because the refusal has to be actionable — collapsing it into `rejected` would lose the
 * difference between "you sent something invalid" and "end these three assignments and try again", and
 * only the second tells the administrator what to do.
 */
export type EdjerWrite =
  | { kind: 'saved'; value: CompassEdjer }
  | { kind: 'rejected'; message: string }
  | { kind: 'blocked'; message: string; assignments: BlockingAssignment[] };

const ADMIN_ROOT = '/api/compass/v1/admin/edjers';

/** The server's message for a rejected write, or a fallback if it sent none. */
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
  } catch {
    // A rejection without a JSON body is still a rejection; fall through to the generic message.
  }

  return 'The EDJEr could not be saved.';
}

/** Reads the outcome of a POST or PUT, including the 422 the deactivation guard answers. */
async function readWrite(response: Response, expected: number): Promise<EdjerWrite> {
  if (response.status === expected) {
    return { kind: 'saved', value: (await response.json()) as CompassEdjer };
  }

  if (response.status === 422) {
    // Read the body ONCE — a Response body cannot be consumed twice, so the message and the assignments
    // come from the same parse rather than from two calls where the second silently fails.
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

/** Every EDJEr, active and inactive — what the administration list renders. */
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

/** One EDJEr's full record, for the form to edit. */
export async function fetchEdjer(id: number): Promise<EdjerLoad<CompassEdjer>> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    // A 404 is a failure to load rather than its own state: the form reached for a record that is not
    // there, and there is nothing for the administrator to do about it but go back.
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as CompassEdjer };
}

/** Adds an EDJEr, who becomes selectable across Compass on save (FR-011). */
export async function createEdjer(request: CompassEdjerRequest): Promise<EdjerWrite> {
  const response = await apiFetch(apiUrl(ADMIN_ROOT), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return readWrite(response, 201);
}

/** Updates an EDJEr. May be refused with 422 when it would deactivate one who holds open assignments. */
export async function updateEdjer(id: number, request: CompassEdjerRequest): Promise<EdjerWrite> {
  const response = await apiFetch(apiUrl(`${ADMIN_ROOT}/${id}`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return readWrite(response, 200);
}

/** The react-query key for the EDJEr collection. */
export function edjersQueryKey(): [string, string] {
  return ['compass', 'edjers'];
}

/** The react-query key for one EDJEr's record. */
export function edjerQueryKey(id: number): [string, string, number] {
  return ['compass', 'edjers', id];
}
