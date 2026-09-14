import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { ClientPicker } from '../../../../src/features/assignments/ClientPicker';

function renderPicker(onChange = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <label htmlFor="client-picker">Client</label>
      <ClientPicker id="client-picker" value="" onChange={onChange} />
    </QueryClientProvider>,
  );
  return onChange;
}

const ROWS = [
  { id: 1, clientName: 'Buckeye Mutual', derivedStatus: 'Active' },
  { id: 2, clientName: 'Brand New — zero assignments', derivedStatus: 'Inactive' },
];

describe('ClientPicker', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('requests the client pickers route', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    renderPicker();

    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith('/api/compass/assignments/pickers/clients'),
    );
  });

  it('lists every client, including one derived Inactive — the O6 named regression test', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    renderPicker();

    expect(await screen.findByRole('option', { name: /buckeye mutual/i })).toBeInTheDocument();
    const zeroAssignment = await screen.findByRole('option', { name: /brand new/i });
    expect(zeroAssignment).toBeInTheDocument();
    // Selectable, not disabled — a zero-assignment client deriving Inactive must not be excluded.
    expect(zeroAssignment).not.toBeDisabled();
  });

  it('shows derived status as context only, never disabling the option', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    renderPicker();

    // Wait for a real, data-driven option — findAllByRole would otherwise resolve immediately
    // against just the always-present placeholder, before the list has actually loaded.
    await screen.findByRole('option', { name: /buckeye mutual/i });
    for (const option of screen.getAllByRole('option')) {
      expect(option).not.toBeDisabled();
    }
  });

  it('calls onChange with the selected client id', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    const onChange = renderPicker();

    await screen.findByRole('option', { name: /buckeye mutual/i });
    await userEvent.selectOptions(screen.getByLabelText('Client'), '1');

    expect(onChange).toHaveBeenCalledWith('1');
  });

  it('disables the select while loading', () => {
    // Resolves eventually (not a permanently-pending promise) so cleanup doesn't hang waiting on
    // an in-flight query when the test ends — only the SYNCHRONOUS initial-render state matters here.
    fetchSpy.mockImplementation(
      () =>
        new Promise((resolve) =>
          setTimeout(() => resolve({ status: 200, json: async () => ROWS }), 20),
        ),
    );
    renderPicker();

    expect(screen.getByLabelText('Client')).toBeDisabled();
  });
});
