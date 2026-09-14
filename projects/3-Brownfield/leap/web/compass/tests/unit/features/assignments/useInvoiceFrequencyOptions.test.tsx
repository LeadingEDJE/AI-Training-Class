import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { useInvoiceFrequencyOptions } from '../../../../src/features/assignments/useInvoiceFrequencyOptions';

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe('useInvoiceFrequencyOptions (US6, #64)', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('reads the ASSIGNMENT-scoped route, not the admin lookup route', async () => {
    // The regression this hook was rewritten for. `/api/compass/v1/admin/invoice-frequency-types` is
    // gated at the Compass root because it CREATES and UPDATES cadences; setting an assignment's
    // override needs only Compass Ops. Reading the admin route made an Ops user authorized to choose
    // a cadence but forbidden from listing the cadences to choose from.
    fetchSpy.mockResolvedValue({ status: 200, json: async () => [{ id: 1, typeName: 'Weekly' }] });

    const { result } = renderHook(() => useInvoiceFrequencyOptions(), { wrapper });

    await waitFor(() => expect(result.current).toHaveLength(1));
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/compass/assignments/pickers/invoice-frequency-types?activeOnly=true',
    );
    expect(fetchSpy).not.toHaveBeenCalledWith(expect.stringContaining('/admin/'));
  });

  it('asks for ACTIVE cadences only, so a retired one is never offered as a new choice', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => [] });

    renderHook(() => useInvoiceFrequencyOptions(), { wrapper });

    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());
    expect(fetchSpy.mock.calls[0][0]).toContain('activeOnly=true');
  });

  it('returns the cadences the server sent', async () => {
    fetchSpy.mockResolvedValue({
      status: 200,
      json: async () => [
        { id: 1, typeName: 'Weekly' },
        { id: 2, typeName: 'Monthly' },
      ],
    });

    const { result } = renderHook(() => useInvoiceFrequencyOptions(), { wrapper });

    await waitFor(() => expect(result.current).toHaveLength(2));
    expect(result.current[1]).toEqual({ id: 2, typeName: 'Monthly' });
  });

  it('degrades to an empty list rather than throwing when the read is refused', async () => {
    // A 403 should no longer happen for anyone who can reach these screens -- but the form must still
    // render if it does. An empty list means the selector offers "Use the client default" alone,
    // which is a usable screen; an exception would take the whole form down.
    fetchSpy.mockResolvedValue({ status: 403, json: async () => ({}) });

    const { result } = renderHook(() => useInvoiceFrequencyOptions(), { wrapper });

    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());
    expect(result.current).toEqual([]);
  });
});
