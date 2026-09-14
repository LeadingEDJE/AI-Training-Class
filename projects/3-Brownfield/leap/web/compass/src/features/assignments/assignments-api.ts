import { apiFetch, apiUrl } from '../../lib/api-url';

/**
 * Typed shapes and calls for the assignment and SOW write surfaces (feature 006,
 * contracts/assignment-write-surface.md, contracts/sow-write-surface.md). Every call goes through
 * `apiFetch`, never a bare, unwrapped `fetch` call (`tests/unit/no-bare-fetch.test.ts`).
 */

/**
 * `POST /api/compass/assignments`. `contracts/assignment-write-surface.md` §2.
 *
 * `invoiceFrequencyTypeId` is the assignment's own invoice-frequency override, added by US6 (#64)
 * once `ClientAssignment.InvoiceFrequencyTypeId` existed. `null` means "no override — bill the way
 * this client does", which is the ordinary case; it is not the same as an absent field.
 */
export interface CreateAssignmentRequest {
  employeeId: number;
  clientId: number;
  startDate: string;
  endDate: string | null;
  note: string | null;
  /** The invoice-frequency override, or null to bill the way the client does (US6, #64). */
  invoiceFrequencyTypeId: number | null;
}

/**
 * `PUT /api/compass/assignments/{id}`. §2. `employeeId`/`clientId` are deliberately absent — moving an
 * assignment to a different EDJEr or client is not a criterion.
 */
export interface UpdateAssignmentRequest {
  startDate: string;
  endDate: string | null;
  note: string | null;
  invoiceFrequencyTypeId: number | null;
}

/**
 * The assignment row as the write surface returns it — viewer-tier projected. §3. `note` is ABSENT
 * (not present as `null`) for a non-elevated viewer; that projection is a server responsibility, not
 * something this type alone can express.
 *
 * `invoiceFrequencyTypeId` and `effectiveInvoiceFrequency` ship with US6 (#64) and are declared
 * below. `sows` (US3, T078+) is part of the contract's full shape but is not yet sent by the server —
 * see `AssignmentRowDto.cs`.
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
   * The CLIENT's internal-EDJE ("beach") flag, denormalised onto the row by the server (issue #518).
   *
   * Not a property of the assignment — it decides whether the SOWs / Contracts card and the invoice
   * frequency surfaces render at all, since internal work has no counterparty to sign a contract
   * with and is never invoiced. Carried here so a screen needs no second fetch of the client;
   * `ClientView.isInternal` does the same job one screen over (issue #243).
   *
   * Always present, unlike `note`: a layout decision, not a disclosure the server may withhold.
   */
  isInternal: boolean;
  note?: string;
  /** This assignment's own override, or null when it sets none (FR-036). */
  invoiceFrequencyTypeId: number | null;
  /**
   * The cadence that actually applies: the override, else the client default, else `null` meaning
   * NONE SET (FR-037, FR-039). Resolved by the server — a screen must render it, never re-derive it
   * from the two ids, or the precedence rule would exist in two places.
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

/**
 * Same shape as a create, except that `LegacyMigrated` is admissible — and only by being sent back
 * UNCHANGED on a row that already holds it (US8/#67, issue #404). The server refuses a type change on
 * a legacy row in both directions: provenance is not editable, so an edit corrects the dates and note
 * and leaves the type alone. A create still cannot name the type at all, which is why this widens
 * `UpdateSowRequest` rather than `CreateSowRequest`.
 */
export type UpdateSowRequest = Omit<CreateSowRequest, 'sowType'> & {
  sowType: CreateSowRequest['sowType'] | 'LegacyMigrated';
};

/**
 * A SOW row as the write surface returns it — viewer-tier projected. §3. `rateIncrease` and `note` are
 * elevated-only; `hasPassedApplicationValidation` is not exposed.
 */
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
 * Client record (AC-1, AC-2). A 404 resolves to `{ kind: 'loaded', value: null }` rather than
 * `'failed'`: it's a legitimate, expected answer for an id that does not exist, not an error.
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

/**
 * `GET /pickers/clients` (US2, #63) — every client, never filtered by derived status. The O6 named
 * regression test: a zero-assignment client MUST appear here, selectable (FR-009-FR-012, BR-11).
 */
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

/**
 * Permanently deletes an assignment and every SOW under it (issue #593) — Compass Super Admin
 * only. A true delete: there is no undo, matching the owner's explicit direction on the issue.
 */
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

/**
 * `GET /api/compass/assignments/{assignmentId}/sows` (FR-014). Unlike the assignment surface, there is
 * no widened read exception here — a refusal means the viewer is not Compass Ops or the root at all
 * (contract §1), not merely that they cannot write.
 */
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

/**
 * Permanently deletes ONE contract period (issue #593) — Compass Super Admin only. The sibling
 * assignment and any other SOWs under it are untouched.
 */
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
