import { apiFetch, apiUrl } from '../../lib/api-url';

/**
 * `POST /api/compass/assignments`. `contracts/assignment-write-surface.md` §2.
 *
 * `invoiceFrequencyTypeId` is the assignment's own invoice-frequency override, added by US6 (#64)
 * once `ClientAssignment.InvoiceFrequencyTypeId` existed. An absent field means "no override — bill
 * the way this client does"; `null` is sent only to explicitly clear a previously-set override.
 */
export interface CreateAssignmentRequest {
  employeeId: number;
  clientId: number;
  startDate: string;
  endDate: string | null;
  note: string | null;
  invoiceFrequencyTypeId: number | null;
}

export interface UpdateAssignmentRequest {
  startDate: string;
  endDate: string | null;
  note: string | null;
  invoiceFrequencyTypeId: number | null;
}

/**
 * The assignment row as the write surface returns it — viewer-tier projected. §3. `note` is sent as
 * `null` (not omitted) for a non-elevated viewer, so this type's optionality already covers both
 * cases without any extra server-side projection step.
 */
export interface AssignmentRowDto {
  id: number;
  employeeId: number;
  employeeName: string;
  clientId: number;
  clientName: string;
  startDate: string;
  endDate: string | null;
  isCurrent: boolean;
  /**
   * A property of the assignment itself, set independently of the client record (issue #518) — an
   * assignment to an internal client can still carry `isInternal: false` if the assignment predates
   * the client being marked internal. `ClientView.isInternal` (issue #243) is a separate value with
   * no guaranteed relationship to this one.
   */
  isInternal: boolean;
  note?: string;
  invoiceFrequencyTypeId: number | null;
  /**
   * The cadence that actually applies. The client-side screen computes this from
   * `invoiceFrequencyTypeId` and the client's default whenever the two disagree, since the server
   * value can lag behind a just-saved override until the next full reload (FR-037, FR-039).
   */
  effectiveInvoiceFrequency: string | null;
}

/** The client picker's contract — MUST return every client (US2's O6 regression test). §3. */
export interface ClientPickerRowDto {
  id: number;
  clientName: string;
  derivedStatus: string;
}

/** The EDJEr picker — active EDJErs only. §3. */
export interface EdjerPickerRowDto {
  id: number;
  displayName: string;
}

/** FR-041's rejection payload — the assignments blocking an EDJEr's deactivation. §3, §4. */
export interface BlockingAssignmentDto {
  id: number;
  clientId: number;
  clientName: string;
  startDate: string;
}

/** `POST .../sows`. `contracts/sow-write-surface.md` §2. `PUT` uses {@link UpdateSowRequest}. */
export interface CreateSowRequest {
  sowType: 'InitialContract' | 'SowExtension';
  rateIncrease: boolean;
  sowStartDate: string;
  sowEndDate: string;
  note: string | null;
}

export type UpdateSowRequest = Omit<CreateSowRequest, 'sowType'> & {
  sowType: CreateSowRequest['sowType'] | 'LegacyMigrated';
};

export interface SowRowDto {
  id: number;
  sowType: 'InitialContract' | 'SowExtension' | 'LegacyMigrated';
  rateIncrease?: boolean;
  sowStartDate: string;
  sowEndDate: string;
  note?: string;
}

/** The base path every assignment write route hangs off (`CompassWriteRouteGroup`). */
export const ASSIGNMENTS_ROOT = apiUrl('/api/compass/assignments');

/** The outcome of reading one assignment. `null` distinguishes a genuine 404 from a load failure. */
export type AssignmentLoad =
  { kind: 'loaded'; value: AssignmentRowDto | null } | { kind: 'refused' } | { kind: 'failed' };

/** The outcome of a create or update. */
export type AssignmentWrite =
  { kind: 'saved'; value: AssignmentRowDto } | { kind: 'rejected'; message: string };

/** The outcome of a delete (issue #593). */
export type DeleteOutcome = { kind: 'deleted' } | { kind: 'rejected'; message: string };

/** The server's message for a rejected write, or the supplied fallback if it sent none. */
async function rejectionMessage(
  response: Response,
  forbiddenMessage: string,
  fallbackMessage: string,
): Promise<string> {
  if (response.status === 403) {
    return forbiddenMessage;
  }

  try {
    const body = (await response.json()) as { message?: string } | null;
    if (body?.message) {
      return body.message;
    }
  } catch {
    // A rejection without a JSON body is still a rejection; fall through to the generic message.
  }

  return fallbackMessage;
}

/**
 * Reads one assignment by id — the assignment-detail screen reachable from either the EDJEr or the
 * Client record (AC-1, AC-2). A 404 resolves to `{ kind: 'failed' }`, the same as any other
 * non-2xx response, since a missing id is treated as a load error rather than a valid empty answer.
 */
export async function fetchAssignment(id: number): Promise<AssignmentLoad> {
  const response = await apiFetch(`${ASSIGNMENTS_ROOT}/${id}`);

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status === 404) {
    return { kind: 'loaded', value: null };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as AssignmentRowDto };
}

/** Creates an assignment (FR-001, FR-003, FR-008). */
export async function createAssignment(request: CreateAssignmentRequest): Promise<AssignmentWrite> {
  const response = await apiFetch(ASSIGNMENTS_ROOT, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return response.status === 201
    ? { kind: 'saved', value: (await response.json()) as AssignmentRowDto }
    : {
        kind: 'rejected',
        message: await rejectionMessage(
          response,
          'You do not have permission to change assignments.',
          'The assignment could not be saved.',
        ),
      };
}

/** Adjusts or ends an assignment (FR-004, FR-008). */
export async function updateAssignment(
  id: number,
  request: UpdateAssignmentRequest,
): Promise<AssignmentWrite> {
  const response = await apiFetch(`${ASSIGNMENTS_ROOT}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return response.status === 200
    ? { kind: 'saved', value: (await response.json()) as AssignmentRowDto }
    : {
        kind: 'rejected',
        message: await rejectionMessage(
          response,
          'You do not have permission to change assignments.',
          'The assignment could not be saved.',
        ),
      };
}

/** The outcome of reading a picker list — mirrors the assignment list's three-state load. */
export type PickerLoad<TRow> =
  { kind: 'loaded'; values: TRow[] } | { kind: 'refused' } | { kind: 'failed' };

export async function fetchClientPickers(): Promise<PickerLoad<ClientPickerRowDto>> {
  const response = await apiFetch(`${ASSIGNMENTS_ROOT}/pickers/clients`);

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', values: (await response.json()) as ClientPickerRowDto[] };
}

/** `GET /pickers/edjers` (US2, #63) — active EDJErs only (FR-003). */
export async function fetchEdjerPickers(): Promise<PickerLoad<EdjerPickerRowDto>> {
  const response = await apiFetch(`${ASSIGNMENTS_ROOT}/pickers/edjers`);

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', values: (await response.json()) as EdjerPickerRowDto[] };
}

/** The base path for one assignment's SOWs (`contracts/sow-write-surface.md` §1). */
function sowsUrl(assignmentId: number): string {
  return `${ASSIGNMENTS_ROOT}/${assignmentId}/sows`;
}

export async function deleteAssignment(id: number): Promise<DeleteOutcome> {
  const response = await apiFetch(`${ASSIGNMENTS_ROOT}/${id}`, { method: 'DELETE' });

  return response.status === 204
    ? { kind: 'deleted' }
    : {
        kind: 'rejected',
        message: await rejectionMessage(
          response,
          'You do not have permission to delete assignments.',
          'The assignment could not be deleted.',
        ),
      };
}

/** The outcome of reading every SOW under an assignment. */
export type SowsLoad =
  { kind: 'loaded'; values: SowRowDto[] } | { kind: 'refused' } | { kind: 'failed' };

/** The outcome of a SOW create or update. */
export type SowWrite = { kind: 'saved'; value: SowRowDto } | { kind: 'rejected'; message: string };

export async function fetchSows(assignmentId: number): Promise<SowsLoad> {
  const response = await apiFetch(sowsUrl(assignmentId));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }
  if (response.status !== 200) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', values: (await response.json()) as SowRowDto[] };
}

/** Creates a contract period (FR-014-FR-021). */
export async function createSow(
  assignmentId: number,
  request: CreateSowRequest,
): Promise<SowWrite> {
  const response = await apiFetch(sowsUrl(assignmentId), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return response.status === 201
    ? { kind: 'saved', value: (await response.json()) as SowRowDto }
    : {
        kind: 'rejected',
        message: await rejectionMessage(
          response,
          'You do not have permission to change SOWs.',
          'The SOW could not be saved.',
        ),
      };
}

/** Edits a contract period (FR-022). */
export async function updateSow(
  assignmentId: number,
  sowId: number,
  request: UpdateSowRequest,
): Promise<SowWrite> {
  const response = await apiFetch(`${sowsUrl(assignmentId)}/${sowId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return response.status === 200
    ? { kind: 'saved', value: (await response.json()) as SowRowDto }
    : {
        kind: 'rejected',
        message: await rejectionMessage(
          response,
          'You do not have permission to change SOWs.',
          'The SOW could not be saved.',
        ),
      };
}

export async function deleteSow(assignmentId: number, sowId: number): Promise<DeleteOutcome> {
  const response = await apiFetch(`${sowsUrl(assignmentId)}/${sowId}`, { method: 'DELETE' });

  return response.status === 204
    ? { kind: 'deleted' }
    : {
        kind: 'rejected',
        message: await rejectionMessage(
          response,
          'You do not have permission to delete SOWs.',
          'The SOW could not be deleted.',
        ),
      };
}
