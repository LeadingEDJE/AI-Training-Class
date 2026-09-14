import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
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
  type AssignmentWrite,
  type CreateAssignmentRequest,
  type CreateSowRequest,
  type DeleteOutcome,
  type SowWrite,
  type UpdateAssignmentRequest,
  type UpdateSowRequest,
} from './assignments-api';
import type { SowListRow } from './SowList';

/**
 * The react-query key for the assignment list. Kept even though no screen lists every assignment
 * anymore (the standalone `/compass/assignments` page was removed, feature 006) — it's still the
 * key `useCreateAssignment`/`useUpdateAssignment` invalidate on a write, in case a future surface
 * (e.g. a reports/dashboard screen) reads it.
 */
export const ASSIGNMENTS_QUERY_KEY = ['compass', 'assignments'];

/** The react-query key for a single assignment. */
export const assignmentQueryKey = (id: number) => ['compass', 'assignment', id];

/**
 * Reads one assignment by id — the detail screen reachable from the EDJEr or Client record
 * (AC-1, AC-2). `null`, not `undefined`, for the LOADED-but-absent case: React Query surfaces an
 * undefined result as an error, which would turn the deliberate 404 case back into "something went
 * wrong". A real failure or refusal, by contrast, MUST throw — collapsing it into the same `null` as
 * a genuine 404 would render "not found" for what is actually a server error or a denied request.
 */
export function useAssignment(id: number) {
  return useQuery({
    queryKey: assignmentQueryKey(id),
    // No retry: a refusal won't succeed on a later attempt, and the app's QueryClient (main.tsx) takes
    // React Query's default retries otherwise — three attempts with backoff before `isError` turns
    // true, which reads as the page hanging rather than a prompt denial.
    retry: false,
    queryFn: async () => {
      const result = await fetchAssignment(id);
      if (result.kind !== 'loaded') {
        throw new Error(`Assignment request ${result.kind} for id ${id}`);
      }
      return result.value;
    },
  });
}

/** Reads every client for the picker (US2, #63) — never filtered by derived status (the O6 test). */
export function useClientPickers() {
  return useQuery({
    queryKey: ['compass', 'client-pickers'],
    queryFn: async () => {
      const result = await fetchClientPickers();
      return result.kind === 'loaded' ? result.values : [];
    },
  });
}

/** Reads active EDJErs for the picker (US2, #63) — FR-003. */
export function useEdjerPickers() {
  return useQuery({
    queryKey: ['compass', 'edjer-pickers'],
    queryFn: async () => {
      const result = await fetchEdjerPickers();
      return result.kind === 'loaded' ? result.values : [];
    },
  });
}

/**
 * Both parent read surfaces (AC-1, AC-2) embed assignment history inline, so a write here must
 * invalidate them too — otherwise the EDJEr/Client record a viewer returns to after saving would
 * keep showing stale history. A bare prefix invalidates every query under it (React Query's default
 * partial match), so this reaches every employee-detail/client-view query regardless of which id.
 */
async function invalidateEmbeddingReadSurfaces(queryClient: ReturnType<typeof useQueryClient>) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['compass', 'employee-detail'] }),
    queryClient.invalidateQueries({ queryKey: ['compass', 'client-view'] }),
  ]);
}

/** Creates an assignment, refreshing the list and the EDJEr/client records it appears on. */
export function useCreateAssignment() {
  const queryClient = useQueryClient();
  return useMutation<AssignmentWrite, Error, CreateAssignmentRequest>({
    mutationFn: createAssignment,
    onSuccess: async (result) => {
      if (result.kind === 'saved') {
        await queryClient.invalidateQueries({ queryKey: ASSIGNMENTS_QUERY_KEY });
        await invalidateEmbeddingReadSurfaces(queryClient);
      }
    },
  });
}

/**
 * Adjusts or ends an assignment, refreshing the list, its own detail query, and the EDJEr/client
 * records it appears on.
 */
export function useUpdateAssignment() {
  const queryClient = useQueryClient();
  return useMutation<AssignmentWrite, Error, { id: number; request: UpdateAssignmentRequest }>({
    mutationFn: ({ id, request }) => updateAssignment(id, request),
    onSuccess: async (result, variables) => {
      if (result.kind === 'saved') {
        await queryClient.invalidateQueries({ queryKey: ASSIGNMENTS_QUERY_KEY });
        await queryClient.invalidateQueries({ queryKey: assignmentQueryKey(variables.id) });
        await invalidateEmbeddingReadSurfaces(queryClient);
      }
    },
  });
}

/**
 * Permanently deletes an assignment and every SOW under it (issue #593), refreshing the list and
 * the EDJEr/client records that embedded it. Does NOT invalidate the assignment's own detail
 * query — the caller navigates away from a page whose subject no longer exists.
 */
export function useDeleteAssignment() {
  const queryClient = useQueryClient();
  return useMutation<DeleteOutcome, Error, number>({
    mutationFn: deleteAssignment,
    onSuccess: async (result) => {
      if (result.kind === 'deleted') {
        await queryClient.invalidateQueries({ queryKey: ASSIGNMENTS_QUERY_KEY });
        await invalidateEmbeddingReadSurfaces(queryClient);
      }
    },
  });
}

/** The react-query key for one assignment's SOWs. */
export const sowsQueryKey = (assignmentId: number) => ['compass', 'sows', assignmentId];

/**
 * Reads every SOW under an assignment (FR-014). `enabled` gates the fetch on the viewer actually
 * being able to reach the route at all — contract §1 grants NO read exception for SOWs (unlike the
 * assignment surface), so a Compass Admin/Sales viewer would only ever see a refusal here.
 */
export function useSows(assignmentId: number, enabled: boolean) {
  return useQuery({
    queryKey: sowsQueryKey(assignmentId),
    enabled,
    retry: false,
    queryFn: async (): Promise<SowListRow[]> => {
      const result = await fetchSows(assignmentId);
      if (result.kind !== 'loaded') {
        throw new Error(`Sows request ${result.kind} for assignment ${assignmentId}`);
      }
      // The wire shape uses ABSENT (undefined), not null, for a withheld elevated-only field
      // (ADR-008 rule 2); SowListRow uses null so SowList can tell "withheld" from "not given"
      // without also needing to know about `undefined`.
      return result.values.map((row) => ({
        id: row.id,
        sowType: row.sowType,
        sowStartDate: row.sowStartDate,
        sowEndDate: row.sowEndDate,
        rateIncrease: row.rateIncrease ?? null,
        note: row.note ?? null,
      }));
    },
  });
}

/**
 * Creates a contract period, refreshing its SOW list and the records that embed it —
 * `EmployeeDetailPage` renders `assignment.sows` straight out of the employee-detail payload.
 */
export function useCreateSow(assignmentId: number) {
  const queryClient = useQueryClient();
  return useMutation<SowWrite, Error, CreateSowRequest>({
    mutationFn: (request) => createSow(assignmentId, request),
    onSuccess: async (result) => {
      if (result.kind === 'saved') {
        await queryClient.invalidateQueries({ queryKey: sowsQueryKey(assignmentId) });
        await invalidateEmbeddingReadSurfaces(queryClient);
      }
    },
  });
}

/** Edits a contract period, refreshing its SOW list and the records that embed it. */
export function useUpdateSow(assignmentId: number) {
  const queryClient = useQueryClient();
  return useMutation<SowWrite, Error, { sowId: number; request: UpdateSowRequest }>({
    mutationFn: ({ sowId, request }) => updateSow(assignmentId, sowId, request),
    onSuccess: async (result) => {
      if (result.kind === 'saved') {
        await queryClient.invalidateQueries({ queryKey: sowsQueryKey(assignmentId) });
        await invalidateEmbeddingReadSurfaces(queryClient);
      }
    },
  });
}

/**
 * Permanently deletes ONE contract period (issue #593), refreshing its SOW list and the records
 * that embed it.
 */
export function useDeleteSow(assignmentId: number) {
  const queryClient = useQueryClient();
  return useMutation<DeleteOutcome, Error, number>({
    mutationFn: (sowId) => deleteSow(assignmentId, sowId),
    onSuccess: async (result) => {
      if (result.kind === 'deleted') {
        await queryClient.invalidateQueries({ queryKey: sowsQueryKey(assignmentId) });
        await invalidateEmbeddingReadSurfaces(queryClient);
      }
    },
  });
}
