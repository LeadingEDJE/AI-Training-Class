import { createElement, type ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('../../../../src/lib/api-url', () => ({
  apiFetch: vi.fn(),
  apiUrl: (path: string) => path,
}));

import { apiFetch } from '../../../../src/lib/api-url';
import {
  useClientPickers,
  useCreateAssignment,
  useCreateSow,
  useDeleteAssignment,
  useDeleteSow,
  useEdjerPickers,
  useSows,
  useUpdateAssignment,
  useUpdateSow,
} from '../../../../src/features/assignments/useAssignments';

const mockApiFetch = vi.mocked(apiFetch);

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
}

/** The wrapper plus the client behind it, for asserting what a write invalidated. */
function createWrapperWithClient() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    queryClient,
    wrapper: ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children),
  };
}

describe('useClientPickers / useEdjerPickers — the refused/failed fallback', () => {
  beforeEach(() => vi.clearAllMocks());

  it('useClientPickers resolves to an empty list when the server refuses (401/403)', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 403 }));

    const { result } = renderHook(() => useClientPickers(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('useEdjerPickers resolves to an empty list when the server refuses (401/403)', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 403 }));

    const { result } = renderHook(() => useEdjerPickers(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });
});

describe('useCreateAssignment / useUpdateAssignment — a rejected write does not invalidate', () => {
  beforeEach(() => vi.clearAllMocks());

  it('useCreateAssignment does not throw and reports "rejected" on a 400', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse({ message: 'Nope' }, 400));

    const { result } = renderHook(() => useCreateAssignment(), { wrapper: createWrapper() });
    const outcome = await result.current.mutateAsync({
      employeeId: 1,
      clientId: 1,
      startDate: '2024-01-01',
      endDate: null,
      note: null,
    });

    expect(outcome).toEqual({ kind: 'rejected', message: 'Nope' });
  });

  it('useUpdateAssignment does not throw and reports "rejected" on a 400', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse({ message: 'Nope' }, 400));

    const { result } = renderHook(() => useUpdateAssignment(), { wrapper: createWrapper() });
    const outcome = await result.current.mutateAsync({
      id: 1,
      request: { startDate: '2024-01-01', endDate: null, note: null },
    });

    expect(outcome).toEqual({ kind: 'rejected', message: 'Nope' });
  });
});

describe('useDeleteAssignment — issue #593', () => {
  beforeEach(() => vi.clearAllMocks());

  it('does not throw and reports "rejected" on a 403', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 403 }));

    const { result } = renderHook(() => useDeleteAssignment(), { wrapper: createWrapper() });
    const outcome = await result.current.mutateAsync(1);

    expect(outcome).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to delete assignments.',
    });
  });

  it('reports "deleted" on 204 and invalidates the list plus the embedding read surfaces', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 204 }));
    const { queryClient, wrapper } = createWrapperWithClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDeleteAssignment(), { wrapper });
    const outcome = await result.current.mutateAsync(1);

    expect(outcome).toEqual({ kind: 'deleted' });
    const keys = invalidate.mock.calls.map(([argument]) => JSON.stringify(argument.queryKey));
    expect(keys).toContain(JSON.stringify(['compass', 'assignments']));
    expect(keys).toContain(JSON.stringify(['compass', 'employee-detail']));
    expect(keys).toContain(JSON.stringify(['compass', 'client-view']));
  });

  it('does NOT invalidate anything on a rejected delete', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 403 }));
    const { queryClient, wrapper } = createWrapperWithClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDeleteAssignment(), { wrapper });
    await result.current.mutateAsync(1);

    expect(invalidate).not.toHaveBeenCalled();
  });
});

describe('useDeleteSow — issue #593', () => {
  beforeEach(() => vi.clearAllMocks());

  it('does not throw and reports "rejected" on a 403', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 403 }));

    const { result } = renderHook(() => useDeleteSow(3), { wrapper: createWrapper() });
    const outcome = await result.current.mutateAsync(1);

    expect(outcome).toEqual({
      kind: 'rejected',
      message: 'You do not have permission to delete SOWs.',
    });
  });

  it('reports "deleted" on 204 and invalidates its list plus the embedding read surfaces', async () => {
    mockApiFetch.mockResolvedValue(new Response(null, { status: 204 }));
    const { queryClient, wrapper } = createWrapperWithClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDeleteSow(3), { wrapper });
    const outcome = await result.current.mutateAsync(1);

    expect(outcome).toEqual({ kind: 'deleted' });
    const keys = invalidate.mock.calls.map(([argument]) => JSON.stringify(argument.queryKey));
    expect(keys).toContain(JSON.stringify(['compass', 'sows', 3]));
    expect(keys).toContain(JSON.stringify(['compass', 'employee-detail']));
    expect(keys).toContain(JSON.stringify(['compass', 'client-view']));
  });
});

describe('useSows — the withheld-field mapping', () => {
  beforeEach(() => vi.clearAllMocks());

  it('maps an absent rateIncrease/note to null, not undefined', async () => {
    mockApiFetch.mockResolvedValue(
      jsonResponse([
        { id: 1, sowType: 'InitialContract', sowStartDate: '2024-04-01', sowEndDate: '2024-12-31' },
      ]),
    );

    const { result } = renderHook(() => useSows(3, true), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0]).toEqual({
      id: 1,
      sowType: 'InitialContract',
      sowStartDate: '2024-04-01',
      sowEndDate: '2024-12-31',
      rateIncrease: null,
      note: null,
    });
  });
});

describe('useCreateSow / useUpdateSow — a rejected write does not invalidate', () => {
  beforeEach(() => vi.clearAllMocks());

  it('useCreateSow does not throw and reports "rejected" on a 409 overlap', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse({ message: 'Overlap' }, 409));

    const { result } = renderHook(() => useCreateSow(3), { wrapper: createWrapper() });
    const outcome = await result.current.mutateAsync({
      sowType: 'InitialContract',
      rateIncrease: false,
      sowStartDate: '2024-04-01',
      sowEndDate: '2024-12-31',
      note: null,
    });

    expect(outcome).toEqual({ kind: 'rejected', message: 'Overlap' });
  });

  it('useUpdateSow does not throw and reports "rejected" on a 409 overlap', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse({ message: 'Overlap' }, 409));

    const { result } = renderHook(() => useUpdateSow(3), { wrapper: createWrapper() });
    const outcome = await result.current.mutateAsync({
      sowId: 1,
      request: {
        sowType: 'InitialContract',
        rateIncrease: false,
        sowStartDate: '2024-04-01',
        sowEndDate: '2024-12-31',
        note: null,
      },
    });

    expect(outcome).toEqual({ kind: 'rejected', message: 'Overlap' });
  });
});

describe('useCreateSow / useUpdateSow — the embedding read surfaces (#448)', () => {
  beforeEach(() => vi.clearAllMocks());

  /**
   * A SOW write used to invalidate its own list and nothing else, so `EmployeeDetailPage` — which
   * renders `assignment.sows` from the employee-detail payload — kept showing the old set.
   */
  it('useCreateSow refreshes the EDJEr and client records the SOW appears on', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse({ id: 5 }, 201));
    const { queryClient, wrapper } = createWrapperWithClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCreateSow(3), { wrapper });

    await result.current.mutateAsync({
      sowType: 'InitialContract',
      rateIncrease: false,
      sowStartDate: '2026-01-01',
      sowEndDate: '2026-12-31',
      note: null,
    });

    const keys = invalidate.mock.calls.map(([argument]) => JSON.stringify(argument.queryKey));
    expect(keys).toContain(JSON.stringify(['compass', 'sows', 3]));
    expect(keys).toContain(JSON.stringify(['compass', 'employee-detail']));
    expect(keys).toContain(JSON.stringify(['compass', 'client-view']));
  });

  it('useUpdateSow refreshes the same surfaces', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse({ id: 5 }, 200));
    const { queryClient, wrapper } = createWrapperWithClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useUpdateSow(3), { wrapper });

    await result.current.mutateAsync({
      sowId: 5,
      request: {
        sowType: 'InitialContract',
        rateIncrease: false,
        sowStartDate: '2026-01-01',
        sowEndDate: '2026-12-31',
        note: null,
      },
    });

    const keys = invalidate.mock.calls.map(([argument]) => JSON.stringify(argument.queryKey));
    expect(keys).toContain(JSON.stringify(['compass', 'sows', 3]));
    expect(keys).toContain(JSON.stringify(['compass', 'employee-detail']));
    expect(keys).toContain(JSON.stringify(['compass', 'client-view']));
  });
});
