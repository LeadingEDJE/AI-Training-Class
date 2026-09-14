import { createElement, type ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('../../../src/lib/api-url', () => ({
  apiFetch: vi.fn(),
  apiUrl: vi.fn((path: string) => path),
}));

import { apiFetch } from '../../../src/lib/api-url';
import { useCurrentUser, currentUserQueryKey } from '../../../src/hooks/useCurrentUser';

const mockApiFetch = vi.mocked(apiFetch);

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createWrapper(client?: QueryClient) {
  const queryClient = client ?? new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
}

const ME = {
  edjeId: '11111111-1111-1111-1111-111111111111',
  email: 'jane@leadingedje.com',
  displayName: 'Jane Doe',
  privileges: ['Compass Super Admin'],
  impersonator: null,
};

describe('useCurrentUser', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('exposes a stable query key for cache invalidation', () => {
    expect(currentUserQueryKey).toEqual(['current-user']);
  });

  it('is loading before the /api/me response resolves', () => {
    mockApiFetch.mockReturnValue(
      new Promise<Response>(() => {
        /* never resolves — keeps the query in its loading state */
      }),
    );
    const { result } = renderHook(() => useCurrentUser(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
    expect(result.current.data).toBeUndefined();
  });

  it('returns identity and privileges from GET /api/me', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse(ME));

    const { result } = renderHook(() => useCurrentUser(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockApiFetch).toHaveBeenCalledWith('/api/me');
    expect(result.current.data).toEqual(ME);
    expect(result.current.data?.privileges).toEqual(['Compass Super Admin']);
  });

  it('surfaces the impersonator block when the session is impersonating', async () => {
    const impersonated = {
      ...ME,
      impersonator: {
        edjeId: '22222222-2222-2222-2222-222222222222',
        displayName: 'Admin Boss',
        email: 'boss@leadingedje.com',
      },
    };
    mockApiFetch.mockResolvedValue(jsonResponse(impersonated));

    const { result } = renderHook(() => useCurrentUser(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.impersonator).toEqual(impersonated.impersonator);
  });

  it('returns null (not an error) when the session probe is unauthenticated (401)', async () => {
    mockApiFetch.mockResolvedValue(new Response('Unauthorized', { status: 401 }));

    const { result } = renderHook(() => useCurrentUser(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBeNull();
    expect(result.current.isError).toBe(false);
  });

  it('is an error state when the server fails (non-401)', async () => {
    mockApiFetch.mockResolvedValue(new Response('boom', { status: 500 }));

    const { result } = renderHook(() => useCurrentUser(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('caches the result — two consumers share one /api/me fetch', async () => {
    mockApiFetch.mockResolvedValue(jsonResponse(ME));
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    });
    const wrapper = createWrapper(client);

    const first = renderHook(() => useCurrentUser(), { wrapper });
    await waitFor(() => expect(first.result.current.isSuccess).toBe(true));

    const second = renderHook(() => useCurrentUser(), { wrapper });
    await waitFor(() => expect(second.result.current.isSuccess).toBe(true));

    expect(mockApiFetch).toHaveBeenCalledTimes(1);
  });
});
