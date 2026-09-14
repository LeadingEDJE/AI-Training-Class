import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { ClientDirectoryPage } from '../../../../src/features/client-directory/ClientDirectoryPage';
import type { ClientDirectoryRow } from '../../../../src/features/client-directory/types';

const ROWS: ClientDirectoryRow[] = [
  { id: 1, clientName: 'Currently Engaged', status: 'Active' },
  { id: 2, clientName: 'All Ended', status: 'Inactive' },
  { id: 3, clientName: 'Never Assigned', status: 'Inactive' },
];

function renderPage(rows: ClientDirectoryRow[] = ROWS) {
  return render(<ClientDirectoryPage rows={rows} isPending={false} isError={false} />);
}

describe('ClientDirectoryPage', () => {
  it('lists every client with its status', () => {
    renderPage();

    const table = screen.getByRole('table', { name: 'Client Directory' });
    expect(within(table).getByText('Currently Engaged')).toBeInTheDocument();
    expect(within(table).getAllByText('Inactive')).toHaveLength(2);
  });

  it('links each client to its client view (AC-12)', () => {
    renderPage();

    expect(screen.getByRole('link', { name: 'Currently Engaged' })).toHaveAttribute(
      'href',
      '/compass/client-directory/1',
    );
  });

  it('wears the shared table-link style, not its own', () => {
    // It shipped without the green underline the Team Directory's carried (owner request 2026-08-21).
    renderPage();

    expect(screen.getByRole('link', { name: 'Currently Engaged' }).className).toBe(tableLinkClass);
  });

  it('shows a status for a client that has never been assigned, not a blank cell', () => {
    // Binary and total (AC-42). A blank here would read as missing data and would not sort.
    renderPage();

    const row = screen.getByRole('row', { name: /Never Assigned/ });
    expect(within(row).getByText('Inactive')).toBeInTheDocument();
  });

  it('conveys status as text rather than colour alone', () => {
    // AC-NFR-5: it has to survive a greyscale render and a screen reader.
    renderPage();

    expect(screen.getByRole('row', { name: /Currently Engaged/ })).toHaveTextContent('Active');
  });

  it('exposes both columns as sortable', async () => {
    const onSortChange = vi.fn();
    const user = userEvent.setup();
    render(
      <ClientDirectoryPage
        rows={ROWS}
        isPending={false}
        isError={false}
        onSortChange={onSortChange}
      />,
    );

    await user.click(screen.getByRole('button', { name: /sort by status/i }));
    await user.click(screen.getByRole('button', { name: /sort by client/i }));

    expect(onSortChange).toHaveBeenCalledWith('status');
    expect(onSortChange).toHaveBeenCalledWith('clientName');
  });

  it('marks the sorted column with aria-sort, as every other Compass table does', () => {
    // The hand-rolled table set no aria-sort at all, so a screen-reader user could activate a sort
    // control and get no confirmation that anything had been ordered. The `Table` primitive carries
    // it; adopting the primitive is what fixes it (feature 008 FR-020).
    //
    // Client leads because that is the SERVER's fallback order — `CompassReadRepository`'s client
    // listing falls through to `OrderBy(ClientName)`. The screen has to name that default to draw the
    // indicator on the right column before anyone has clicked anything.
    renderPage();

    const table = screen.getByRole('table', { name: 'Client Directory' });
    expect(within(table).getByRole('columnheader', { name: /client/i })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    expect(within(table).getByRole('columnheader', { name: /status/i })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('moves the sort indicator when the server orders by another column', () => {
    render(
      <ClientDirectoryPage
        rows={ROWS}
        isPending={false}
        isError={false}
        sort="status"
        descending
      />,
    );

    const table = screen.getByRole('table', { name: 'Client Directory' });
    expect(within(table).getByRole('columnheader', { name: /status/i })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
    expect(within(table).getByRole('columnheader', { name: /client/i })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('sizes its heading like every other Compass page', () => {
    // Two h1 sizes shipped: `text-xl` from `PageHeader` and `text-2xl` hand-rolled here and on the
    // client view. Same product, two title sizes, decided by which file you happened to be in.
    renderPage();

    const heading = screen.getByRole('heading', { level: 1, name: 'Client Directory' });
    expect(heading.className).toContain('text-xl');
    expect(heading.className).not.toContain('text-2xl');
  });

  it('reports a search change to its caller', async () => {
    const onSearchChange = vi.fn();
    const user = userEvent.setup();
    render(
      <ClientDirectoryPage
        rows={ROWS}
        isPending={false}
        isError={false}
        onSearchChange={onSearchChange}
      />,
    );

    await user.type(screen.getByLabelText(/search by client name/i), 'end');

    expect(onSearchChange).toHaveBeenCalled();
  });

  it('tolerates callbacks being absent', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /sort by status/i }));
    await user.type(screen.getByLabelText(/search by client name/i), 'a');

    expect(screen.getByRole('table', { name: 'Client Directory' })).toBeInTheDocument();
  });

  it('shows an explicit empty state', () => {
    renderPage([]);

    expect(screen.getByText(/no clients match/i)).toBeInTheDocument();
  });

  it('offers a status filter defaulting to All', () => {
    renderPage();

    expect(screen.getByRole('combobox', { name: /status/i })).toHaveValue('all');
    expect(screen.getByText('Currently Engaged')).toBeInTheDocument();
    expect(screen.getByText('Never Assigned')).toBeInTheDocument();
  });

  it('filters by status in the browser (issue #640)', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'Active');

    expect(screen.getByText('Currently Engaged')).toBeInTheDocument();
    expect(screen.queryByText('All Ended')).not.toBeInTheDocument();
    expect(screen.queryByText('Never Assigned')).not.toBeInTheDocument();
  });

  it('works together with the server-driven search, narrowing further (issue #640)', async () => {
    const user = userEvent.setup();
    const onSearchChange = vi.fn();
    render(
      <ClientDirectoryPage
        rows={ROWS}
        isPending={false}
        isError={false}
        onSearchChange={onSearchChange}
      />,
    );

    await user.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'Inactive');

    expect(screen.getByText('All Ended')).toBeInTheDocument();
    expect(screen.getByText('Never Assigned')).toBeInTheDocument();
    expect(screen.queryByText('Currently Engaged')).not.toBeInTheDocument();

    // The search box still reports to its caller, entirely independently of the local status filter.
    await user.type(screen.getByLabelText(/search by client name/i), 'end');
    expect(onSearchChange).toHaveBeenCalled();
  });

  it('shows a loading state', () => {
    render(<ClientDirectoryPage rows={[]} isPending isError={false} />);

    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows an error state', () => {
    render(<ClientDirectoryPage rows={[]} isPending={false} isError />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});
