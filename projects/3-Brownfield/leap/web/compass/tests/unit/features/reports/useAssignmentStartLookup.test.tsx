import { render, renderHook, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { useAssignmentStartLookup } from '../../../../src/features/reports/useAssignmentStartLookup';
import { AssignmentStartRoute } from '../../../../src/features/reports/assignment-start/AssignmentStartRoute';

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const ROWS = [
  { employeeName: 'Aisha Thompson', clientName: 'Buckeye Mutual', startDate: '2026-01-05' },
];

describe('useAssignmentStartLookup (US4, #78)', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('issues NO request until a range is supplied', async () => {
    // The whole point of the nullable range. This report answers a question the user poses, so a
    // fetch on mount would put rows on screen for a range nobody chose.
    renderHook(() => useAssignmentStartLookup(null), { wrapper });

    await Promise.resolve();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('requests the range it was given, both bounds, unchanged', async () => {
    fetchSpy.mockResolvedValue({ status: 200, ok: true, json: async () => ROWS });

    const { result } = renderHook(
      () => useAssignmentStartLookup({ from: '2026-03-01', to: '2026-03-31' }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.data?.kind).toBe('loaded'));
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/compass/reports/assignment-start?from=2026-03-01&to=2026-03-31',
    );
  });

  it('returns the rows the server sent', async () => {
    fetchSpy.mockResolvedValue({ status: 200, ok: true, json: async () => ROWS });

    const { result } = renderHook(
      () => useAssignmentStartLookup({ from: '2026-01-01', to: '2026-12-31' }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.data?.kind).toBe('loaded'));
    expect(result.current.data).toEqual({ kind: 'loaded', value: ROWS });
  });

  it('resolves a 403 as REFUSED rather than throwing', async () => {
    // Resolving means react-query sees a success and never retries, so a refusal is asked exactly
    // once — and the screen can tell "you may not see this" apart from "it broke".
    fetchSpy.mockResolvedValue({ status: 403, ok: false, json: async () => ({}) });

    const { result } = renderHook(
      () => useAssignmentStartLookup({ from: '2026-03-01', to: '2026-03-31' }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.data).toEqual({ kind: 'refused' }));
  });

  it('resolves a 401 as refused too, so an expiring session does not render a hard error', async () => {
    fetchSpy.mockResolvedValue({ status: 401, ok: false, json: async () => ({}) });

    const { result } = renderHook(
      () => useAssignmentStartLookup({ from: '2026-03-01', to: '2026-03-31' }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.data).toEqual({ kind: 'refused' }));
  });

  it('resolves any other non-OK status as FAILED', async () => {
    fetchSpy.mockResolvedValue({ status: 500, ok: false, json: async () => ({}) });

    const { result } = renderHook(
      () => useAssignmentStartLookup({ from: '2026-03-01', to: '2026-03-31' }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.data).toEqual({ kind: 'failed' }));
  });

  it('keys the cache by the range, so a second range is not served the first answer', async () => {
    // Sharing one key would hand range B the rows for range A, which reads from the screen exactly
    // like the server filtering wrongly.
    fetchSpy.mockResolvedValue({ status: 200, ok: true, json: async () => ROWS });

    const { rerender, result } = renderHook(
      ({ from, to }: { from: string; to: string }) => useAssignmentStartLookup({ from, to }),
      { wrapper, initialProps: { from: '2026-03-01', to: '2026-03-31' } },
    );

    await waitFor(() => expect(result.current.data?.kind).toBe('loaded'));
    rerender({ from: '2026-04-01', to: '2026-04-30' });

    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith(
        '/api/compass/reports/assignment-start?from=2026-04-01&to=2026-04-30',
      ),
    );
  });
});

describe('AssignmentStartRoute (US4, #78)', () => {
  beforeEach(() => fetchSpy.mockReset());

  // `{ selector: 'input' }` on every Start Date query below, matching `AssignmentStartReport.test.tsx`:
  // `getByLabelText` also matches an `aria-label`, and since issue #461 the Assignment Start Date
  // header carries "Sort by assignment start date". Each call here happens to precede Run on a
  // freshly-rendered route, so the ambiguity cannot bite today — scoped anyway, because the mitigation
  // is the cheap half and a case that acquires results before touching the field again is an ordinary
  // thing to write next.
  it('renders the form without fetching, then looks up the submitted range', async () => {
    fetchSpy.mockResolvedValue({ status: 200, ok: true, json: async () => ROWS });

    render(<AssignmentStartRoute />, { wrapper });

    // Nothing asked yet.
    expect(fetchSpy).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText(/start date/i, { selector: 'input' }), '2026-01-01');
    await userEvent.type(screen.getByLabelText(/end date/i), '2026-12-31');
    await userEvent.click(screen.getByRole('button', { name: 'Run' }));

    await waitFor(() => expect(screen.getByText('Aisha Thompson')).toBeInTheDocument());
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/compass/reports/assignment-start?from=2026-01-01&to=2026-12-31',
    );
  });

  it('does not reach the server at all when the range is inverted', async () => {
    // The route half of spec US4 scenario 2: the page rejects it, so no request is ever made.
    render(<AssignmentStartRoute />, { wrapper });

    await userEvent.type(screen.getByLabelText(/start date/i, { selector: 'input' }), '2026-12-31');
    await userEvent.type(screen.getByLabelText(/end date/i), '2026-01-01');
    await userEvent.click(screen.getByRole('button', { name: 'Run' }));

    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('surfaces a refusal from the server', async () => {
    fetchSpy.mockResolvedValue({ status: 403, ok: false, json: async () => ({}) });

    render(<AssignmentStartRoute />, { wrapper });

    await userEvent.type(screen.getByLabelText(/start date/i, { selector: 'input' }), '2026-01-01');
    await userEvent.type(screen.getByLabelText(/end date/i), '2026-12-31');
    await userEvent.click(screen.getByRole('button', { name: 'Run' }));

    await waitFor(() =>
      expect(screen.getByText('You do not have access to this report.')).toBeInTheDocument(),
    );
  });

  it('surfaces a failure from the server', async () => {
    fetchSpy.mockResolvedValue({ status: 500, ok: false, json: async () => ({}) });

    render(<AssignmentStartRoute />, { wrapper });

    await userEvent.type(screen.getByLabelText(/start date/i, { selector: 'input' }), '2026-01-01');
    await userEvent.type(screen.getByLabelText(/end date/i), '2026-12-31');
    await userEvent.click(screen.getByRole('button', { name: 'Run' }));

    await waitFor(() =>
      expect(screen.getByText('The lookup could not be run. Try again.')).toBeInTheDocument(),
    );
  });
});
