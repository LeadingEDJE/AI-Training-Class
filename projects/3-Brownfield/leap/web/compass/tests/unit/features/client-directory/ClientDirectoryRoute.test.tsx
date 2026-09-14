import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { ClientDirectoryRoute } from '../../../../src/features/client-directory/ClientDirectoryRoute';
import type { ClientDirectoryRow } from '../../../../src/features/client-directory/types';

/**
 * The route, not the page.
 *
 * `ClientDirectoryPage` already has its own tests, and every one of them passes the sort props
 * DIRECTLY — which is exactly why the page could ship announcing a sort the server was not using.
 * The defect lived in the wiring between the two, so it needs a test that renders the pair together
 * and drives them through a real click. The Team Directory has had one since feature 005
 * (`TeamDirectoryRoute.test.tsx`); this file closes the same gap on the Client Directory.
 */

const DIRECTORY: ClientDirectoryRow[] = [
  { id: 1, clientName: 'Acme Industrial', status: 'Active' },
  { id: 2, clientName: 'Borealis Freight', status: 'Inactive' },
];

function renderRoute() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ClientDirectoryRoute />
    </QueryClientProvider>,
  );
}

/** Every URL the route has requested. */
function requestedUrls(): string[] {
  return fetchSpy.mock.calls.map((call) => String(call[0]));
}

describe('ClientDirectoryRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    fetchSpy.mockImplementation(() =>
      Promise.resolve({ ok: true, status: 200, json: async () => DIRECTORY }),
    );
  });

  it('marks the column it is sorting by, so the header agrees with the request', async () => {
    // The decisive one. Without the route passing `sort`, the page falls back to its own
    // DEFAULT_SORT_COLUMN and reports Client as ascending forever — so a screen-reader user
    // activating "Sort by status" is told the table is ordered by a column it is not ordered by.
    // That is worse than the missing aria-sort this screen was retrofitted to fix.
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Sort by status' }));

    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('sort=status'))).toBe(true),
    );
    expect(screen.getByRole('columnheader', { name: 'Status' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    // And the column it is NOT ordering by must stop claiming it is.
    expect(screen.getByRole('columnheader', { name: 'Client' })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('announces descending after the same column is chosen twice', async () => {
    // `descending` has its own wire. Passing `sort` alone would satisfy the test above and still
    // announce "ascending" on a table the server has reversed.
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    const sortByClient = screen.getByRole('button', { name: 'Sort by client' });
    await user.click(sortByClient);
    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('sort=clientName'))).toBe(true),
    );

    await user.click(sortByClient);

    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('desc=true'))).toBe(true),
    );
    expect(screen.getByRole('columnheader', { name: 'Client' })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });

  it('sends the search term to the server rather than filtering locally', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());

    await user.type(screen.getByLabelText('Search by Client Name'), 'acme');

    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('search=acme'))).toBe(true),
    );
  });

  it('filters by status locally, sending no request to the server (issue #640)', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());
    const before = requestedUrls().length;

    await user.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'Active');

    expect(screen.getByText('Acme Industrial')).toBeInTheDocument();
    expect(screen.queryByText('Borealis Freight')).not.toBeInTheDocument();
    expect(requestedUrls()).toHaveLength(before);
  });

  it('surfaces a failed request as an error state', async () => {
    fetchSpy.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});
