import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const routeParams = vi.hoisted(() => ({ value: { clientId: '1' } as Record<string, string> }));
const navigateSpy = vi.fn();
const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  // The route container fetches the invoice-frequency cadences for the override select (US6, #64).
  // Answered here with an EMPTY list rather than through `fetchSpy`, because every stub in this file
  // is a catch-all keyed on `/api/me` and would otherwise hand the lookup an assignment payload. The
  // override select's own behaviour is covered directly in `AssignmentForm.test.tsx`.
  apiFetch: (url: string, init?: RequestInit) =>
    url.includes('invoice-frequency-types')
      ? Promise.resolve({ status: 200, json: async () => [] })
      : fetchSpy(url, init),
}));

vi.mock('@tanstack/react-router', () => ({
  useParams: () => routeParams.value,
  useNavigate: () => navigateSpy,
}));

import { NewClientAssignmentRoute } from '../../../../src/features/assignments/NewClientAssignmentRoute';

function renderRoute(clientId = 1) {
  routeParams.value = { clientId: String(clientId) };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <NewClientAssignmentRoute />
    </QueryClientProvider>,
  );
}

const CLIENT_VIEW = {
  id: 1,
  clientName: 'Buckeye Mutual',
  status: 'Active',
  assignmentHistory: [],
};

describe('NewClientAssignmentRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    navigateSpy.mockReset();
  });

  it('fixes the client from the route param, using the client view for the display name', async () => {
    fetchSpy.mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('/pickers/edjers')
          ? { status: 200, ok: true, json: async () => [] }
          : { status: 200, ok: true, json: async () => CLIENT_VIEW },
      ),
    );
    renderRoute();

    expect(await screen.findByRole('heading', { name: 'New Assignment' })).toBeInTheDocument();
    expect(screen.getAllByText('Buckeye Mutual').length).toBeGreaterThan(0);
  });

  it('surfaces an error state when the client cannot be loaded', async () => {
    fetchSpy.mockResolvedValue({ status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('navigates to the new assignment detail screen on a successful save', async () => {
    fetchSpy.mockImplementation((url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve({
          status: 201,
          json: async () => ({
            id: 42,
            employeeId: 9,
            employeeName: 'Maya Alvarez',
            clientId: 1,
            clientName: 'Buckeye Mutual',
            startDate: '2026-01-01',
            endDate: null,
            isCurrent: true,
            // Issue #518 — the created row carries the client's internal-EDJE flag.
            isInternal: false,
          }),
        });
      }
      if (url.includes('/pickers/edjers')) {
        return Promise.resolve({
          status: 200,
          json: async () => [{ id: 9, displayName: 'Maya Alvarez' }],
        });
      }
      return Promise.resolve({ status: 200, ok: true, json: async () => CLIENT_VIEW });
    });
    renderRoute();

    await screen.findByRole('option', { name: 'Maya Alvarez' });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'EDJEr' }), '9');
    await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() =>
      expect(navigateSpy).toHaveBeenCalledWith({
        to: '/client-directory/$clientId/assignments/$assignmentId',
        params: { clientId: '1', assignmentId: '42' },
      }),
    );
  });

  it('shows the rejection and does not navigate when the save is refused', async () => {
    fetchSpy.mockImplementation((url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve({
          status: 400,
          json: async () => ({ message: 'The end date must be on or after the start date.' }),
        });
      }
      if (url.includes('/pickers/edjers')) {
        return Promise.resolve({
          status: 200,
          json: async () => [{ id: 9, displayName: 'Maya Alvarez' }],
        });
      }
      return Promise.resolve({ status: 200, ok: true, json: async () => CLIENT_VIEW });
    });
    renderRoute();

    await screen.findByRole('option', { name: 'Maya Alvarez' });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'EDJEr' }), '9');
    await userEvent.type(screen.getByLabelText(/start date/i), '2026-06-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The end date must be on or after the start date.',
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
