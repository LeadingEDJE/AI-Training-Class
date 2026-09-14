import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import { buildClientDirectoryQuery } from '../../../../src/features/client-directory/useClientDirectory';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { ClientDirectoryRoute } from '../../../../src/features/client-directory/ClientDirectoryRoute';

describe('buildClientDirectoryQuery', () => {
  it('returns an empty string when nothing is constrained', () => {
    expect(buildClientDirectoryQuery({})).toBe('');
  });

  it('omits blank values and trims what it sends', () => {
    expect(buildClientDirectoryQuery({ search: '  ' })).toBe('');
    expect(buildClientDirectoryQuery({ search: ' end ' })).toBe('?search=end');
  });

  it('includes sort and direction', () => {
    expect(buildClientDirectoryQuery({ sort: 'status', desc: true })).toBe(
      '?sort=status&desc=true',
    );
  });

  it('omits desc when ascending', () => {
    expect(buildClientDirectoryQuery({ sort: 'status', desc: false })).toBe('?sort=status');
  });
});

describe('ClientDirectoryRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    fetchSpy.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: 1, clientName: 'Currently Engaged', status: 'Active' }],
    });
  });

  function renderRoute() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={queryClient}>
        <ClientDirectoryRoute />
      </QueryClientProvider>,
    );
  }

  it('requests the client read surface', async () => {
    renderRoute();

    await waitFor(() =>
      expect(fetchSpy.mock.calls[0][0]).toContain('/api/compass/client-directory'),
    );
  });

  it('sends the search term to the server rather than filtering locally', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());

    await user.type(screen.getByLabelText(/search by client name/i), 'end');

    await waitFor(() =>
      expect(fetchSpy.mock.calls.some((c) => String(c[0]).includes('search=end'))).toBe(true),
    );
  });

  it('flips sort direction when the same column is chosen twice', async () => {
    const user = userEvent.setup();
    renderRoute();
    const sortByStatus = await screen.findByRole('button', { name: /sort by status/i });

    await user.click(sortByStatus);
    await waitFor(
      () =>
        expect(fetchSpy.mock.calls.some((c) => String(c[0]).includes('sort=status'))).toBe(true),
      { timeout: 3000 },
    );

    await user.click(sortByStatus);
    await waitFor(
      () => expect(fetchSpy.mock.calls.some((c) => String(c[0]).includes('desc=true'))).toBe(true),
      { timeout: 3000 },
    );
  });

  it('surfaces a failed request as an error state', async () => {
    fetchSpy.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});
