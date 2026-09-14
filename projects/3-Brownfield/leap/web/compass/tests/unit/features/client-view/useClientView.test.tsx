import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const routeParams = vi.hoisted(() => ({ value: { clientId: '1' } as Record<string, string> }));
const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

vi.mock('@tanstack/react-router', () => ({
  useParams: () => routeParams.value,
}));

import { ClientViewRoute } from '../../../../src/features/client-view/ClientViewRoute';

function renderRoute(clientId = 1) {
  routeParams.value = { clientId: String(clientId) };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ClientViewRoute />
    </QueryClientProvider>,
  );
}

const VIEW = {
  id: 1,
  clientName: 'Currently Engaged',
  status: 'Active',
  assignmentHistory: [],
};

describe('ClientViewRoute', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('requests the client by id', async () => {
    fetchSpy.mockResolvedValue({ ok: true, status: 200, json: async () => VIEW });
    renderRoute(7);

    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith('/api/compass/client-directory/7'));
  });

  it('renders the client when the server returns one', async () => {
    fetchSpy.mockResolvedValue({ ok: true, status: 200, json: async () => VIEW });
    renderRoute();

    expect(await screen.findByRole('heading', { name: 'Currently Engaged' })).toBeInTheDocument();
  });

  it('treats a 404 as not found rather than an error', async () => {
    fetchSpy.mockResolvedValue({ ok: false, status: 404, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByText(/not found/i)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('surfaces a real failure as an error state', async () => {
    fetchSpy.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});
