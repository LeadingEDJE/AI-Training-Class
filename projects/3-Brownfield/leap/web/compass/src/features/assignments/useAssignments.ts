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
 * The react-query key for the standalone `/compass/assignments` list screen (feature 006).
 * `useCreateAssignment`/`useUpdateAssignment` invalidate it so that screen stays current.
 */
export const ASSIGNMENTS_QUERY_KEY = ['compass', 'assignments'];

/** The react-query key for a single assignment. */
export const assignmentQueryKey = (id: number) => ['compass', 'assignment', id];

export function useAssignment(id: number) {
  return useQuery({
    queryKey: assignmentQueryKey(id),
    // Retries are left at the app's QueryClient default here, since a transient network blip is
    // more likely for this query than a genuine refusal.
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
 * Permanently deletes an assignment and every SOW under it (issue #593), refreshing the list, the
 * EDJEr/client records that embedded it, and its own detail query so a cached copy never lingers.
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
      // The wire shape and `SowListRow` both use null for a withheld elevated-only field
      // (ADR-008 rule 2), so this mapping is a direct passthrough rather than a translation.
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
