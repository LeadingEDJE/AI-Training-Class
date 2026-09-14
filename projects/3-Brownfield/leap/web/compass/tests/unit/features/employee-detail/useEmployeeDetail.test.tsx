import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const routeParams = vi.hoisted(() => ({ value: { employeeId: '2' } as Record<string, string> }));
const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

vi.mock('@tanstack/react-router', () => ({
  useParams: () => routeParams.value,
}));

import { EmployeeDetailRoute } from '../../../../src/features/employee-detail/EmployeeDetailRoute';

function renderRoute(employeeId = 2) {
  routeParams.value = { employeeId: String(employeeId) };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <EmployeeDetailRoute />
    </QueryClientProvider>,
  );
}

describe('EmployeeDetailRoute', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('requests the record by id', async () => {
    fetchSpy.mockResolvedValue({ ok: true, status: 200, json: async () => detail() });
    renderRoute(7);

    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith('/api/compass/team-directory/7'));
  });

  it('renders the record when the server returns one', async () => {
    fetchSpy.mockResolvedValue({ ok: true, status: 200, json: async () => detail() });
    renderRoute();

    expect(await screen.findByRole('heading', { name: 'Otto Other' })).toBeInTheDocument();
  });

  it('treats a 404 as "not found" rather than an error', async () => {
    // The server answers 404 both for a missing record and for one this viewer may not see (FR-021).
    // Rendering "something went wrong" would misreport a legitimate, expected answer.
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

function detail() {
  return {
    id: 2,
    firstName: 'Otto',
    lastName: 'Other',
    hireDate: '2021-02-01',
    email: 'otto.other@example.test',
    employeeType: 'Full Time',
    coachId: 9,
    coach: 'Cody Coach',
    state: 'OH',
    assignmentHistory: [],
    directReports: [],
  };
}
