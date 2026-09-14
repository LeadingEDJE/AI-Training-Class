import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const routeParams = vi.hoisted(() => ({
  value: { clientId: '1', assignmentId: '3' } as Record<string, string>,
}));
const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  // The route container fetches the invoice-frequency cadences for the override select (US6, #64).
  // Answered here with an EMPTY list rather than through `fetchSpy`, because every stub in this file
  // is a catch-all keyed on `/api/me` and would otherwise hand the lookup an assignment payload. The
  // override select's own behaviour is covered directly in `AssignmentForm.test.tsx`.
  apiFetch: (url: string) =>
    url.includes('invoice-frequency-types')
      ? Promise.resolve({ status: 200, json: async () => [] })
      : fetchSpy(url),
}));

const navigateSpy = vi.hoisted(() => vi.fn());
vi.mock('@tanstack/react-router', () => ({
  useParams: () => routeParams.value,
  useNavigate: () => navigateSpy,
}));

import { ClientAssignmentRoute } from '../../../../src/features/assignments/ClientAssignmentRoute';

function renderRoute(clientId = 1, assignmentId = 3) {
  routeParams.value = { clientId: String(clientId), assignmentId: String(assignmentId) };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ClientAssignmentRoute />
    </QueryClientProvider>,
  );
}

const ASSIGNMENT = {
  id: 3,
  employeeId: 9,
  employeeName: 'Maya Alvarez',
  clientId: 1,
  clientName: 'Buckeye Mutual',
  startDate: '2024-04-01',
  endDate: null,
  isCurrent: true,
  // Issue #518 — stated rather than left absent: these cases describe an ORDINARY client, which is
  // what makes the SOWs / Contracts assertions below meaningful. An internal one hides that card.
  isInternal: false,
};

function currentUser(privileges: string[]) {
  return {
    status: 200,
    ok: true,
    json: async () => ({
      edjeId: 'edje-1',
      email: 'viewer@example.test',
      displayName: 'Viewer',
      privileges,
      impersonator: null,
    }),
  };
}

describe('ClientAssignmentRoute', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('requests the assignment by id, not by client id', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ASSIGNMENT });
    renderRoute();

    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith('/api/compass/assignments/3'));
  });

  it('renders the assignment when the server returns one, breadcrumbed from the client', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ASSIGNMENT });
    renderRoute();

    expect(await screen.findByRole('heading', { name: 'Client Assignment' })).toBeInTheDocument();
    const breadcrumb = screen.getByRole('navigation', { name: /breadcrumb/i });
    expect(breadcrumb).toHaveTextContent('Client Directory');
  });

  it('treats a 404 as not found rather than an error', async () => {
    fetchSpy.mockResolvedValue({ status: 404, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByText(/not found/i)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('surfaces a real failure as an error state', async () => {
    fetchSpy.mockResolvedValue({ status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('submits an edit through useUpdateAssignment, addressed by the assignment id', async () => {
    fetchSpy.mockImplementation((url?: string) =>
      Promise.resolve(
        url?.includes('/api/me') === true
          ? currentUser(['Compass Ops'])
          : { status: 200, json: async () => ({ ...ASSIGNMENT, note: 'Adjusted via test' }) },
      ),
    );
    renderRoute();

    await screen.findByRole('heading', { name: 'Client Assignment' });
    await userEvent.type(await screen.findByLabelText(/note/i), 'Adjusted via test');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith('/api/compass/assignments/3'));
  });

  describe('viewers who cannot edit (AC-16/FR-025 — Compass Admin/Sales can VIEW, not write)', () => {
    it('renders the dates and note as text, with no Save button', async () => {
      fetchSpy.mockImplementation((url?: string) =>
        Promise.resolve(
          url?.includes('/api/me') === true
            ? currentUser(['Compass Sales'])
            : { status: 200, json: async () => ({ ...ASSIGNMENT, note: 'A recorded note' }) },
        ),
      );
      renderRoute();

      await screen.findByRole('heading', { name: 'Client Assignment' });
      expect(await screen.findByText('A recorded note')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/note/i)).not.toBeInTheDocument();
    });

    it('never requests SOWs — contract §1 grants no read exception for them', async () => {
      fetchSpy.mockImplementation((url?: string) =>
        Promise.resolve(
          url?.includes('/api/me') === true
            ? currentUser(['Compass Sales'])
            : { status: 200, json: async () => ASSIGNMENT },
        ),
      );
      renderRoute();

      await screen.findByRole('heading', { name: 'Client Assignment' });
      expect(fetchSpy).not.toHaveBeenCalledWith(expect.stringContaining('/sows'));
    });
  });

  describe('SOWs / Contracts (US3, #66)', () => {
    it('requests SOWs nested under the assignment id for a viewer who can manage assignments', async () => {
      fetchSpy.mockImplementation((url?: string) =>
        Promise.resolve(
          url?.includes('/api/me') === true
            ? currentUser(['Compass Ops'])
            : url?.includes('/sows') === true
              ? { status: 200, json: async () => [] }
              : { status: 200, json: async () => ASSIGNMENT },
        ),
      );
      renderRoute();

      await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith('/api/compass/assignments/3/sows'));
    });

    it('creates a SOW through useCreateSow, addressed by the assignment id, and closes the modal', async () => {
      // The `apiFetch` mock above forwards only the url to `fetchSpy` (no `init`), so the method
      // can't be read off the call — the first hit to the sows route is the initial GET (empty
      // list), the second is this test's POST. Ordering distinguishes them instead.
      const CREATED_SOW = {
        id: 1,
        sowType: 'InitialContract',
        sowStartDate: '2024-04-01',
        sowEndDate: '2024-12-31',
        rateIncrease: false,
      };
      let sowsRequestCount = 0;
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Ops']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          sowsRequestCount += 1;
          return Promise.resolve(
            sowsRequestCount === 1
              ? { status: 200, json: async () => [] }
              : { status: 201, json: async () => CREATED_SOW },
          );
        }
        return Promise.resolve({ status: 200, json: async () => ASSIGNMENT });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /add sow/i }));
      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(sowsRequestCount).toBeGreaterThanOrEqual(2);
    });

    it('edits a SOW through useUpdateSow, addressed by the sow id', async () => {
      const EXISTING_SOW = {
        id: 7,
        sowType: 'SowExtension',
        sowStartDate: '2025-01-01',
        sowEndDate: '2025-12-31',
        rateIncrease: true,
        note: 'FY25 renewal',
      };
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Ops']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          return Promise.resolve({ status: 200, json: async () => [EXISTING_SOW] });
        }
        if (url === '/api/compass/assignments/3/sows/7') {
          return Promise.resolve({
            status: 200,
            json: async () => ({ ...EXISTING_SOW, note: 'Renewed again' }),
          });
        }
        return Promise.resolve({ status: 200, json: async () => ASSIGNMENT });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /edit/i }));
      const dialog = screen.getByRole('dialog');
      await userEvent.clear(within(dialog).getByLabelText(/note/i));
      await userEvent.type(within(dialog).getByLabelText(/note/i), 'Renewed again');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('deletes a SOW through useDeleteSow, addressed by the sow id (issue #593)', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      const EXISTING_SOW = {
        id: 7,
        sowType: 'SowExtension',
        sowStartDate: '2025-01-01',
        sowEndDate: '2025-12-31',
        rateIncrease: true,
        note: 'FY25 renewal',
      };
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Super Admin']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          return Promise.resolve({ status: 200, json: async () => [EXISTING_SOW] });
        }
        if (url === '/api/compass/assignments/3/sows/7') {
          return Promise.resolve({ status: 204, json: async () => undefined });
        }
        return Promise.resolve({ status: 200, json: async () => ASSIGNMENT });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));

      await waitFor(() =>
        expect(fetchSpy).toHaveBeenCalledWith('/api/compass/assignments/3/sows/7'),
      );
      vi.restoreAllMocks();
    });
  });

  describe('Delete Assignment (issue #593)', () => {
    afterEach(() => {
      vi.restoreAllMocks();
      navigateSpy.mockReset();
    });

    it('omits the Delete Assignment action for a Compass Ops viewer', async () => {
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Ops']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          return Promise.resolve({ status: 200, json: async () => [] });
        }
        return Promise.resolve({ status: 200, json: async () => ASSIGNMENT });
      });
      renderRoute();

      await screen.findByRole('heading', { name: 'Client Assignment' });
      expect(screen.queryByRole('button', { name: /delete assignment/i })).not.toBeInTheDocument();
    });

    it('deletes the assignment by id, as a Compass Super Admin, and navigates back to the client', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      let assignmentRequestCount = 0;
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Super Admin']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          return Promise.resolve({ status: 200, json: async () => [] });
        }
        if (url === '/api/compass/assignments/3') {
          assignmentRequestCount += 1;
          // The initial load is a GET; the button click issues the DELETE. This mock forwards only
          // the url (no method), so call order is what distinguishes them, matching this file's own
          // `sowsRequestCount` pattern above.
          return Promise.resolve(
            assignmentRequestCount === 1
              ? { status: 200, json: async () => ASSIGNMENT }
              : { status: 204, json: async () => undefined },
          );
        }
        return Promise.resolve({ status: 404, json: async () => ({}) });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /delete assignment/i }));

      await waitFor(() =>
        expect(navigateSpy).toHaveBeenCalledWith({
          to: '/client-directory/$clientId',
          params: { clientId: '1' },
        }),
      );
    });

    it('does not navigate when the delete is refused', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      let assignmentRequestCount = 0;
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Super Admin']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          return Promise.resolve({ status: 200, json: async () => [] });
        }
        if (url === '/api/compass/assignments/3') {
          assignmentRequestCount += 1;
          return Promise.resolve(
            assignmentRequestCount === 1
              ? { status: 200, json: async () => ASSIGNMENT }
              : { status: 403, json: async () => ({}) },
          );
        }
        return Promise.resolve({ status: 404, json: async () => ({}) });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /delete assignment/i }));

      await waitFor(() => expect(assignmentRequestCount).toBeGreaterThanOrEqual(2));
      expect(navigateSpy).not.toHaveBeenCalled();
      // Not navigating is only half of it: the outcome has to reach the page, or the user confirms
      // the popup and sees nothing happen at all.
      expect(
        await screen.findByText('You do not have permission to delete assignments.'),
      ).toBeInTheDocument();
    });

    it('does not report a failed delete when only the navigation afterwards fails', async () => {
      // The assignment is permanently gone by the time we navigate, so a navigation failure
      // reported as "could not be deleted" tells the user an irreversible delete did not happen
      // (PR #604 review, finding 1). A router loader or `beforeLoad` throwing a non-redirect error
      // is the real shape of this.
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      navigateSpy.mockRejectedValue(new Error('a route loader threw'));
      let assignmentRequestCount = 0;
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Super Admin']));
        }
        if (url?.endsWith('/sows') === true) {
          return Promise.resolve({ status: 200, json: async () => [] });
        }
        if (url === '/api/compass/assignments/3') {
          assignmentRequestCount += 1;
          return Promise.resolve(
            assignmentRequestCount === 1
              ? { status: 200, json: async () => ASSIGNMENT }
              : { status: 204, json: async () => undefined },
          );
        }
        return Promise.resolve({ status: 404, json: async () => ({}) });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /delete assignment/i }));

      // Waiting on the diagnostic is what orders this: it is written from the catch, so the
      // rejection has definitely been handled by the time the absence below is asserted.
      await waitFor(() => expect(consoleError).toHaveBeenCalled());
      expect(screen.queryByText('The assignment could not be deleted.')).not.toBeInTheDocument();
    });

    it('does not carry a refusal banner across a change of assignment id', async () => {
      // Neither assignment route sets `remountDeps` and the router sets no default, so the router
      // REUSES this component instance when only `$assignmentId` changes. The page's own `key` is
      // what resets its local state — without it, assignment 4 renders under assignment 3's
      // refusal banner (PR #604 review, finding 5).
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      const OTHER_ASSIGNMENT = { ...ASSIGNMENT, id: 4, clientName: 'Iron Range Steel' };
      let assignmentRequestCount = 0;
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Super Admin']));
        }
        if (url?.endsWith('/sows') === true) {
          return Promise.resolve({ status: 200, json: async () => [] });
        }
        if (url === '/api/compass/assignments/4') {
          return Promise.resolve({ status: 200, json: async () => OTHER_ASSIGNMENT });
        }
        if (url === '/api/compass/assignments/3') {
          assignmentRequestCount += 1;
          return Promise.resolve(
            assignmentRequestCount === 1
              ? { status: 200, json: async () => ASSIGNMENT }
              : { status: 403, json: async () => ({}) },
          );
        }
        return Promise.resolve({ status: 404, json: async () => ({}) });
      });
      // A fresh `render` per id would remount the tree by itself and prove nothing; `rerender`
      // against one QueryClient is what reproduces a same-route parameter change. The element is
      // rebuilt each time because React bails out of a referentially identical one.
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const tree = () => (
        <QueryClientProvider client={queryClient}>
          <ClientAssignmentRoute />
        </QueryClientProvider>
      );
      routeParams.value = { clientId: '1', assignmentId: '3' };
      const { rerender } = render(tree());

      await userEvent.click(await screen.findByRole('button', { name: /delete assignment/i }));
      expect(
        await screen.findByText('You do not have permission to delete assignments.'),
      ).toBeInTheDocument();

      routeParams.value = { clientId: '1', assignmentId: '4' };
      rerender(tree());

      // Wait for the NEW assignment to be on screen first: asserting absence while its query is
      // still pending would pass on the loading state alone.
      expect((await screen.findAllByText('Iron Range Steel')).length).toBeGreaterThan(0);
      expect(
        screen.queryByText('You do not have permission to delete assignments.'),
      ).not.toBeInTheDocument();
    });
  });

  describe('Delete SOW (issue #593)', () => {
    afterEach(() => {
      vi.restoreAllMocks();
    });

    it('surfaces a refused SOW delete on the page', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      const EXISTING_SOW = {
        id: 7,
        sowType: 'SowExtension',
        sowStartDate: '2025-01-01',
        sowEndDate: '2025-12-31',
        rateIncrease: true,
        note: 'FY25 renewal',
      };
      fetchSpy.mockImplementation((url?: string) => {
        if (url?.includes('/api/me') === true) {
          return Promise.resolve(currentUser(['Compass Super Admin']));
        }
        if (url === '/api/compass/assignments/3/sows') {
          return Promise.resolve({ status: 200, json: async () => [EXISTING_SOW] });
        }
        if (url === '/api/compass/assignments/3/sows/7') {
          return Promise.resolve({ status: 403, json: async () => ({}) });
        }
        return Promise.resolve({ status: 200, json: async () => ASSIGNMENT });
      });
      renderRoute();

      await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));

      expect(
        await screen.findByText('You do not have permission to delete SOWs.'),
      ).toBeInTheDocument();
    });
  });
});
