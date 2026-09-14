import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const routeSearch = vi.hoisted(() => ({ value: {} as { from?: 'admin' } }));
const routeParams = vi.hoisted(() => ({ value: { employeeId: '9' } as Record<string, string> }));
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
  useSearch: () => routeSearch.value,
  useNavigate: () => navigateSpy,
}));

import { NewEmployeeAssignmentRoute } from '../../../../src/features/assignments/NewEmployeeAssignmentRoute';

function renderRoute(employeeId = 9, search: { from?: 'admin' } = {}) {
  routeParams.value = { employeeId: String(employeeId) };
  routeSearch.value = search;
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <NewEmployeeAssignmentRoute />
    </QueryClientProvider>,
  );
}

const EMPLOYEE_DETAIL = {
  id: 9,
  firstName: 'Maya',
  lastName: 'Alvarez',
  hireDate: '2020-01-01',
  email: 'maya@example.test',
  employeeType: 'Full Time',
  coach: null,
  state: 'OH',
  assignmentHistory: [],
};

describe('NewEmployeeAssignmentRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    navigateSpy.mockReset();
  });

  it('fixes the EDJEr from the route param, using the EDJEr detail for the display name', async () => {
    fetchSpy.mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('/pickers/clients')
          ? { status: 200, ok: true, json: async () => [] }
          : { status: 200, ok: true, json: async () => EMPLOYEE_DETAIL },
      ),
    );
    renderRoute();

    expect(await screen.findByRole('heading', { name: 'New Assignment' })).toBeInTheDocument();
    expect(screen.getAllByText('Maya Alvarez').length).toBeGreaterThan(0);
  });

  it('surfaces an error state when the EDJEr cannot be loaded', async () => {
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
      if (url.includes('/pickers/clients')) {
        return Promise.resolve({
          status: 200,
          json: async () => [{ id: 1, clientName: 'Buckeye Mutual', derivedStatus: 'Active' }],
        });
      }
      return Promise.resolve({ status: 200, ok: true, json: async () => EMPLOYEE_DETAIL });
    });
    renderRoute();

    await screen.findByRole('option', { name: /buckeye mutual/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Client' }), '1');
    await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() =>
      expect(navigateSpy).toHaveBeenCalledWith({
        to: '/team-directory/$employeeId/assignments/$assignmentId',
        params: { employeeId: '9', assignmentId: '42' },
        // Undefined, not absent: the directory origin IS the absence of one.
        search: { from: undefined },
      }),
    );
  });

  describe('reached from the EDJEr admin record (#448)', () => {
    it('breadcrumbs back to the admin form rather than the Team Directory', async () => {
      fetchSpy.mockImplementation((url: string) =>
        Promise.resolve(
          url.includes('/pickers/clients')
            ? { status: 200, ok: true, json: async () => [] }
            : { status: 200, ok: true, json: async () => EMPLOYEE_DETAIL },
        ),
      );
      renderRoute(9, { from: 'admin' });

      const trail = await screen.findByRole('navigation', { name: /breadcrumb/i });

      expect(trail).toHaveTextContent(
        'Admin / EDJEr Configuration / Maya Alvarez / New Assignment',
      );
      expect(within(trail).getByRole('link', { name: 'Maya Alvarez' })).toHaveAttribute(
        'href',
        '/compass/admin/edjers/9',
      );
      expect(within(trail).queryByRole('link', { name: 'Team Directory' })).not.toBeInTheDocument();
    });

    it('carries the origin onward, so the assignment it creates keeps the same trail', async () => {
      // Otherwise the trail would be right for one screen and revert the moment they saved.
      fetchSpy.mockImplementation((url: string, init?: RequestInit) =>
        Promise.resolve(
          init?.method === 'POST'
            ? { status: 201, ok: true, json: async () => ({ id: 42 }) }
            : url.includes('/pickers/clients')
              ? { status: 200, ok: true, json: async () => [] }
              : { status: 200, ok: true, json: async () => EMPLOYEE_DETAIL },
        ),
      );
      renderRoute(9, { from: 'admin' });

      await screen.findByRole('heading', { name: 'New Assignment' });
      await userEvent.type(screen.getByLabelText(/start date/i), '2026-09-01');
      await userEvent.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() =>
        expect(navigateSpy).toHaveBeenCalledWith(
          expect.objectContaining({ search: { from: 'admin' } }),
        ),
      );
    });
  });

  it('shows the rejection and does not navigate when the save is refused', async () => {
    fetchSpy.mockImplementation((url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve({
          status: 400,
          json: async () => ({ message: 'The end date must be on or after the start date.' }),
        });
      }
      if (url.includes('/pickers/clients')) {
        return Promise.resolve({
          status: 200,
          json: async () => [{ id: 1, clientName: 'Buckeye Mutual', derivedStatus: 'Active' }],
        });
      }
      return Promise.resolve({ status: 200, ok: true, json: async () => EMPLOYEE_DETAIL });
    });
    renderRoute();

    await screen.findByRole('option', { name: /buckeye mutual/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Client' }), '1');
    await userEvent.type(screen.getByLabelText(/start date/i), '2026-06-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The end date must be on or after the start date.',
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
