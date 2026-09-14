import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

// The router is stubbed so these assert the screen, not routing. Route resolution is router.test.ts's.
//
// The stub INTERPOLATES params into the path, because that is what TanStack Router does — a stub that
// rendered `to` verbatim would make `/admin/edjers/$edjerId` look like a working href and hide a missing
// `params` prop entirely. It also forwards every other prop (className, aria-label) as the real `Link`
// does by spreading them onto the anchor.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    params,
    children,
    ...rest
  }: {
    to: string;
    params?: Record<string, string>;
    children: React.ReactNode;
  } & Record<string, unknown>) => {
    const href = Object.entries(params ?? {}).reduce(
      (path, [key, value]) => path.replace(`$${key}`, value),
      to,
    );
    return (
      <a href={href} {...rest}>
        {children}
      </a>
    );
  },
}));

const { EdjerListPage } = await import('../../../../src/features/edjers/EdjerListPage');

const EDJERS = '/api/compass/v1/admin/edjers';

/**
 * The EDJEr administration list.
 *
 * Its columns now match Team Directory's own (issue #659) — Hire Date, a Type pill, Coach and State —
 * plus Email, which the owner asked to keep. Every column sorts, client-side (issue #666), and the
 * table opens sorted by hire date ascending — oldest hires first, matching Team Directory's own
 * default. Still not the Team Directory itself: no employee-type filter, and no "Current Client(s)"
 * column (which would need assignment reads belonging to another stream).
 */
function edjer(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 7,
    firstName: 'Maya',
    lastName: 'Alvarez',
    email: 'maya.alvarez@example.test',
    employeeTypeName: 'Full Time',
    isActive: true,
    hireDate: '2019-03-14',
    stateOfResidence: 'CA',
    coachName: 'Jordan Wells',
    ...overrides,
  };
}

/**
 * `count` distinct EDJErs, surnames zero-padded so alphabetical order matches creation order.
 *
 * Padding matters: without it "Edjer10" sorts before "Edjer2", and a pagination test asserting which rows
 * land on page 2 would be asserting against string collation rather than against the page boundary.
 */
function manyEdjers(count: number) {
  return Array.from({ length: count }, (_, index) => {
    const n = String(index + 1).padStart(3, '0');
    return edjer({
      id: index + 1,
      firstName: `First${n}`,
      lastName: `Edjer${n}`,
      email: `edjer${n}@example.test`,
    });
  });
}

function renderPage(stub: FetchByUrlStub) {
  vi.stubGlobal('fetch', stub.mock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <EdjerListPage />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('EdjerListPage', () => {
  it('puts the list on the white card surface, not directly on the shell', async () => {
    // Owner report 2026-08-18: this screen's search row and table sat on the #f4f5f6 shell, so the
    // table's own header tint was the only thing separating the list from the page. Asserted through
    // the table's ancestry so it survives re-nesting that keeps the sheet.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    const table = await screen.findByRole('table', { name: 'EDJErs' });

    expect(table.closest('.bg-white')).not.toBeNull();
  });

  it('names the screen', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [] } }));

    expect(await screen.findByRole('heading', { name: /edjers/i, level: 1 })).toBeInTheDocument();
  });

  it('offers a way to add one', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [] } }));

    // The mockups' "+ Add New EDJEr" primary action, in the page header.
    const link = await screen.findByRole('link', { name: /^\+ add new edjer$/i });
    expect(link).toHaveAttribute('href', '/admin/edjers/new');
  });

  it('carries no subtitle explaining the obvious (issue #408)', async () => {
    // Shipped as a mockup `.note` reading "Every EDJEr, active and inactive. Changes take effect
    // immediately." — an owner-requested removal, since it told an administrator nothing the screen
    // itself did not already.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [] } }));

    await screen.findByRole('heading', { name: /edjers/i, level: 1 });

    expect(screen.queryByText(/every edjer, active and inactive/i)).not.toBeInTheDocument();

    // BOTH wordings, because the note this screen carried CHANGED between #408 being raised and it
    // being merged: #406 replaced it with "Defaults to active EDJErs — use the status filter to
    // reach former ones." Guarding only the original text would have watched for a sentence nobody
    // would reintroduce while the one actually on the screen walked back in unopposed.
    //
    // Removing that second wording is deliberate rather than a merge casualty: #406 also added the
    // Status select, which RENDERS its current value ("Active") right beside the list — so the prose
    // restated what the control already says, which is exactly #408's criterion.
    expect(screen.queryByText(/defaults to active edjers/i)).not.toBeInTheDocument();
  });

  it('lists each EDJEr with the columns matching Team Directory, plus email (issue #659)', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: { status: 200, body: [edjer()] },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    const headers = within(table)
      .getAllByRole('columnheader')
      // The caret on the sorted column is an aria-hidden indicator, not part of the label — same
      // stripping TeamDirectoryPage.test.tsx does. Direction is asserted through `aria-sort` below.
      .map((header) => header.textContent?.replace(/[▲▼]/g, '').trim());
    expect(headers).toEqual([
      'First Name',
      'Last Name',
      'Email',
      'Hire Date',
      'Type',
      'Coach',
      'State',
      'Status',
    ]);

    const row = within(table).getAllByRole('row')[1];
    const cells = within(row).getAllByRole('cell');
    expect(cells[0]).toHaveTextContent('Maya');
    expect(cells[1]).toHaveTextContent('Alvarez');
    expect(within(table).getByText('maya.alvarez@example.test')).toBeInTheDocument();
    expect(within(table).getByText('03/14/2019')).toBeInTheDocument();
    expect(within(table).getByText('Full Time')).toBeInTheDocument();
    expect(within(table).getByText('Jordan Wells')).toBeInTheDocument();
    expect(within(table).getByText('CA')).toBeInTheDocument();
  });

  it("shows an EDJEr's type as a pill, matching Team Directory's own Type column (issue #659)", async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    const table = await screen.findByRole('table', { name: /edjers/i });

    // StatusPill renders the word in a `<span>`, distinguishing it from a plain text cell — the same
    // element Team Directory's own Type column uses, so the two screens read as one system.
    const pill = within(table).getByText('Full Time');
    expect(pill.tagName).toBe('SPAN');
  });

  it('states status as a word, never as colour alone', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer(), edjer({ id: 8, lastName: 'Petrov', isActive: false })],
        },
      }),
    );

    // Two statuses in the fixture, but the default Active filter hides one — widen it first.
    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'all');

    expect(within(table).getByText('Active')).toBeInTheDocument();
    // "Former", not "Inactive" — matches the status filter in this same file and every other
    // EDJEr status pill (EdjerFormPage, TeamDirectoryPage, EmployeeDetailPage). Issue #407.
    expect(within(table).getByText('Former')).toBeInTheDocument();
  });

  it('lists INACTIVE EDJErs too once the status filter is widened, because an unseen one can never be reactivated', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer({ id: 8, lastName: 'Petrov', isActive: false })],
        },
      }),
    );

    // The default Active filter leaves nothing to show — no table at all, only the empty state.
    await screen.findByText(/no edjers match the current filters/i);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'all');

    const table = await screen.findByRole('table', { name: /edjers/i });
    expect(within(table).getByRole('cell', { name: 'Petrov' })).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------- the status filter

  it('issue #406: defaults to showing only ACTIVE EDJErs', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer(), edjer({ id: 8, lastName: 'Petrov', isActive: false })],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });

    expect(within(table).getByRole('cell', { name: 'Alvarez' })).toBeInTheDocument();
    expect(within(table).queryByRole('cell', { name: 'Petrov' })).not.toBeInTheDocument();
  });

  it('offers Active, Former and All as status options, defaulting to Active', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    await screen.findByRole('table', { name: /edjers/i });

    const statusFilter = screen.getByRole('combobox', { name: /status/i });
    for (const label of ['Active', 'Former', 'All']) {
      expect(within(statusFilter).getByRole('option', { name: label })).toBeInTheDocument();
    }
    expect(statusFilter).toHaveValue('active');
  });

  it('shows only FORMER EDJErs when Former is selected', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer(), edjer({ id: 8, lastName: 'Petrov', isActive: false })],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'inactive');

    expect(within(table).getByRole('cell', { name: 'Petrov' })).toBeInTheDocument();
    expect(within(table).queryByRole('cell', { name: 'Alvarez' })).not.toBeInTheDocument();
  });

  it('shows every EDJEr, active and former, when All is selected', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer(), edjer({ id: 8, lastName: 'Petrov', isActive: false })],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'all');

    expect(within(table).getByRole('cell', { name: 'Alvarez' })).toBeInTheDocument();
    expect(within(table).getByRole('cell', { name: 'Petrov' })).toBeInTheDocument();
  });

  it('combines the status filter with the search, rather than either alone', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 8, lastName: 'Petrov', isActive: false }),
            edjer({ id: 9, lastName: 'Petrenko', isActive: true }),
          ],
        },
      }),
    );

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'all');
    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'petr');

    expect(screen.getByRole('cell', { name: 'Petrov' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Petrenko' })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'active');

    expect(screen.queryByRole('cell', { name: 'Petrov' })).not.toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Petrenko' })).toBeInTheDocument();
  });

  it('returns to the first page when the status filter changes', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(screen.getByRole('cell', { name: 'Edjer021' })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'all');

    expect(screen.getByRole('cell', { name: 'Edjer001' })).toBeInTheDocument();
  });

  it('says nothing matched the status filter, rather than showing an empty table', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'inactive');

    expect(screen.getByText(/no edjers match the current filters/i)).toBeInTheDocument();
    expect(screen.queryByText(/no edjers yet/i)).not.toBeInTheDocument();
  });

  it('hyperlinks the first and last name to open the record for editing (issue #659)', async () => {
    // Matches Team Directory's own drill-in: both the First Name and Last Name cells link, each with
    // the plain name as its text — not a trailing "Edit" action column.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    await screen.findByRole('table', { name: /edjers/i });

    const firstNameLink = screen.getByRole('link', { name: 'Maya' });
    const lastNameLink = screen.getByRole('link', { name: 'Alvarez' });
    expect(firstNameLink).toHaveAttribute('href', '/admin/edjers/7');
    expect(lastNameLink).toHaveAttribute('href', '/admin/edjers/7');

    expect(screen.queryByRole('link', { name: /^edit/i })).not.toBeInTheDocument();
  });

  it('wears the shared table-link style on both name cells', async () => {
    // The same class run `ClientDirectoryPage` and `TeamDirectoryPage` use for their own name drill-in.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    await screen.findByRole('table', { name: /edjers/i });

    expect(screen.getByRole('link', { name: 'Maya' }).className).toBe(tableLinkClass);
    expect(screen.getByRole('link', { name: 'Alvarez' }).className).toBe(tableLinkClass);
  });

  it("shows the coach's name as plain text, never a link, on the admin screen (issue #659)", async () => {
    // Unlike Team Directory's own Coach column, which links into the coach's record — the owner asked
    // for plain text here specifically.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    const table = await screen.findByRole('table', { name: /edjers/i });

    expect(within(table).getByText('Jordan Wells')).toBeInTheDocument();
    expect(within(table).queryByRole('link', { name: /jordan wells/i })).not.toBeInTheDocument();
  });

  it('shows an empty coach cell when the EDJEr has no coach', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: { status: 200, body: [edjer({ coachName: null })] },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    const row = within(table).getAllByRole('row')[1];
    const cells = within(row).getAllByRole('cell');

    // Coach is the 6th column: First Name, Last Name, Email, Hire Date, Type, Coach.
    expect(cells[5]).toHaveTextContent('');
  });

  // ------------------------------------------------------------------------------ the four states

  it('says so while loading', () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [] } }));

    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('says so when there are none, rather than showing a bare header row', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [] } }));

    expect(await screen.findByText(/no edjers yet/i)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('reports a refusal as a refusal, NOT as an empty list', async () => {
    // The nav hides this screen from a lesser role, but a deep link does not — so this state is
    // reachable, and rendering "no EDJErs yet" for it would tell a Super Admin their directory had
    // vanished.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 403 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
    expect(screen.queryByText(/no edjers yet/i)).not.toBeInTheDocument();
  });

  it('reports a failure to load', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 500 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded/i);
  });

  // ---------------------------------------------------------------------------------- the search

  it('filters by name as the administrator types', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer(), edjer({ id: 8, firstName: 'Devon', lastName: 'Brooks' })],
        },
      }),
    );

    await screen.findByRole('table', { name: /edjers/i });

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'brook');

    expect(screen.getByRole('cell', { name: 'Brooks' })).toBeInTheDocument();
    expect(screen.queryByRole('cell', { name: 'Alvarez' })).not.toBeInTheDocument();
  });

  it('filters by email as well as by name', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer(),
            edjer({ id: 8, firstName: 'Devon', lastName: 'Brooks', email: 'devon@example.test' }),
          ],
        },
      }),
    );

    await screen.findByRole('table', { name: /edjers/i });

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'devon@');

    expect(screen.getByRole('cell', { name: 'Brooks' })).toBeInTheDocument();
    expect(screen.queryByRole('cell', { name: 'Alvarez' })).not.toBeInTheDocument();
  });

  it('matches case-insensitively, because nobody types a surname in the stored case', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    await screen.findByRole('table', { name: /edjers/i });

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'ALVAREZ');

    expect(screen.getByRole('cell', { name: 'Alvarez' })).toBeInTheDocument();
  });

  it('says nothing matched, rather than showing an empty table', async () => {
    // Distinct from "no EDJErs yet": one means the directory is empty, the other means the search was.
    // Showing the same message for both would tell an administrator their data had gone.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    await screen.findByRole('table', { name: /edjers/i });

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'zzzz');

    expect(screen.getByText(/no edjers match/i)).toBeInTheDocument();
    expect(screen.queryByText(/no edjers yet/i)).not.toBeInTheDocument();
  });

  it('filters in the browser rather than re-querying the server', async () => {
    // ~90 EDJErs, so the whole set is already in hand. A request per keystroke would be slower AND would
    // need debouncing to avoid hammering the endpoint.
    const stub = stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } });
    renderPage(stub);

    await screen.findByRole('table', { name: /edjers/i });
    const before = stub.calls().length;

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'alv');

    expect(stub.calls().length).toBe(before);
  });
  // ---------------------------------------------------------------------- sortable columns (#666)

  it('opens sorted by hire date ascending — oldest hires first', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Newest', hireDate: '2023-01-01' }),
            edjer({ id: 2, lastName: 'Oldest', hireDate: '2015-06-15' }),
            edjer({ id: 3, lastName: 'Middle', hireDate: '2019-03-14' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Oldest',
      'Middle',
      'Newest',
    ]);
  });

  it('marks Hire Date as the sorted column by default, and no other column as sorted', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: [edjer()] } }));

    const table = await screen.findByRole('table', { name: /edjers/i });
    expect(within(table).getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    expect(within(table).getByRole('columnheader', { name: 'Last Name' })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('sorts by another column, ascending, when its header is clicked', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer({ id: 1, lastName: 'Zephyr' }), edjer({ id: 2, lastName: 'Alvarez' })],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by last name' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Alvarez',
      'Zephyr',
    ]);
    expect(within(table).getByRole('columnheader', { name: 'Last Name' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    expect(within(table).getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('reverses direction when the same header is clicked again', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [edjer({ id: 1, lastName: 'Zephyr' }), edjer({ id: 2, lastName: 'Alvarez' })],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    const lastNameHeader = screen.getByRole('button', { name: 'Sort by last name' });
    await userEvent.click(lastNameHeader);
    await userEvent.click(lastNameHeader);

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Zephyr',
      'Alvarez',
    ]);
    expect(within(table).getByRole('columnheader', { name: 'Last Name' })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });

  it('resets to ascending when a different header is clicked next', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Zephyr', hireDate: '2015-06-15' }),
            edjer({ id: 2, lastName: 'Alvarez', hireDate: '2023-01-01' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    // Sort by last name descending first, then switch columns — the new column must start ascending
    // rather than inheriting the direction the last click left behind.
    const lastNameHeader = screen.getByRole('button', { name: 'Sort by last name' });
    await userEvent.click(lastNameHeader);
    await userEvent.click(lastNameHeader);

    await userEvent.click(screen.getByRole('button', { name: 'Sort by hire date' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Zephyr',
      'Alvarez',
    ]);
    expect(within(table).getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
  });

  it('breaks a last-name tie by first name when sorting by last name', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, firstName: 'Zoe', lastName: 'Kim' }),
            edjer({ id: 2, firstName: 'Amir', lastName: 'Kim' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by last name' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[0].textContent)).toEqual([
      'Amir',
      'Zoe',
    ]);
  });

  it('sorts in the browser rather than re-querying the server', async () => {
    const stub = stubFetchByUrl({
      [`GET ${EDJERS}`]: {
        status: 200,
        body: [edjer({ id: 1 }), edjer({ id: 2, lastName: 'Zed' })],
      },
    });
    renderPage(stub);

    await screen.findByRole('table', { name: /edjers/i });
    const before = stub.calls().length;

    await userEvent.click(screen.getByRole('button', { name: 'Sort by last name' }));

    expect(stub.calls().length).toBe(before);
  });

  it('sorts an EDJEr with no coach to the end, ahead of every real coach name, either direction', async () => {
    // Three rows and both directions, so the comparator sees a null on the LEFT of a pairing as well
    // as on the right, and sees two real coach names against each other too.
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Bell', coachName: 'Bob Coach' }),
            edjer({ id: 2, lastName: 'Amos', coachName: 'Amy Coach' }),
            edjer({ id: 3, lastName: 'Nocoach', coachName: null }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    const coachHeader = screen.getByRole('button', { name: 'Sort by coach' });
    await userEvent.click(coachHeader);

    const ascending = within(table).getAllByRole('row').slice(1);
    expect(ascending.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Amos',
      'Bell',
      'Nocoach',
    ]);

    await userEvent.click(coachHeader);

    const descending = within(table).getAllByRole('row').slice(1);
    expect(descending.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Nocoach',
      'Bell',
      'Amos',
    ]);
  });

  it('sorts by first name when its header is clicked', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, firstName: 'Zena', lastName: 'Zephyr' }),
            edjer({ id: 2, firstName: 'Amir', lastName: 'Alvarez' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by first name' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[0].textContent)).toEqual([
      'Amir',
      'Zena',
    ]);
  });

  it('sorts by email when its header is clicked', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Zephyr', email: 'zed@example.test' }),
            edjer({ id: 2, lastName: 'Alvarez', email: 'aya@example.test' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by email' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Alvarez',
      'Zephyr',
    ]);
  });

  it('sorts by type when its header is clicked', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Zephyr', employeeTypeName: 'Part Time' }),
            edjer({ id: 2, lastName: 'Alvarez', employeeTypeName: 'Full Time' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by type' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Alvarez',
      'Zephyr',
    ]);
  });

  it('sorts by state when its header is clicked', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Zephyr', stateOfResidence: 'WA' }),
            edjer({ id: 2, lastName: 'Alvarez', stateOfResidence: 'CA' }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by state' }));

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Alvarez',
      'Zephyr',
    ]);
  });

  it('sorts by status when its header is clicked', async () => {
    renderPage(
      stubFetchByUrl({
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            edjer({ id: 1, lastName: 'Zephyr', isActive: true }),
            edjer({ id: 2, lastName: 'Alvarez', isActive: false }),
          ],
        },
      }),
    );

    const table = await screen.findByRole('table', { name: /edjers/i });
    // Widen the status filter first — the default Active view would hide the Former row entirely.
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'all');
    await userEvent.click(screen.getByRole('button', { name: 'Sort by status' }));

    // "Active" sorts before "Former" alphabetically, which is also active-first — the more intuitive
    // reading for an administrator scanning the roster.
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getAllByRole('cell')[1].textContent)).toEqual([
      'Zephyr',
      'Alvarez',
    ]);
  });

  // -------------------------------------------------------------------------------- pagination

  it('defaults to showing every row, not a paged subset (issue #548)', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });

    expect(screen.getAllByRole('link', { name: /^First\d{3}$/ })).toHaveLength(25);
    expect(screen.getByRole('cell', { name: 'Edjer001' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Edjer025' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: /pagination/i })).not.toBeInTheDocument();
  });

  it('offers 20, 50, 100 and All as page sizes, defaulting to All', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });

    const perPage = screen.getByRole('combobox', { name: /per page/i });
    for (const label of ['20', '50', '100', 'All']) {
      expect(within(perPage).getByRole('option', { name: label })).toBeInTheDocument();
    }
    expect(perPage).toHaveValue('all');
  });

  it('shows fewer rows once a smaller page size is chosen', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(60) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '50');

    expect(screen.getAllByRole('link', { name: /^First\d{3}$/ })).toHaveLength(50);
  });

  it('shows every row again once All is chosen after a smaller size', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(130) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '50');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), 'all');

    expect(screen.getAllByRole('link', { name: /^First\d{3}$/ })).toHaveLength(130);
    // And no page controls, because there is nothing to page through.
    expect(screen.queryByRole('navigation', { name: /pagination/i })).not.toBeInTheDocument();
  });

  it('pages forward and back once a smaller size is chosen', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');

    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    expect(screen.getByRole('cell', { name: 'Edjer021' })).toBeInTheDocument();
    expect(screen.queryByRole('cell', { name: 'Edjer001' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /^First\d{3}$/ })).toHaveLength(5);

    await userEvent.click(screen.getByRole('button', { name: /previous/i }));

    expect(screen.getByRole('cell', { name: 'Edjer001' })).toBeInTheDocument();
  });

  it('disables the page controls at each end rather than hiding them', async () => {
    // Disabled and present keeps the layout stable and keeps the control discoverable; hiding it makes the
    // row jump and leaves a keyboard user wondering where focus went.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');

    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /next/i })).toBeEnabled();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    expect(screen.getByRole('button', { name: /previous/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
  });

  it('says which rows are being shown, and out of how many', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');

    expect(screen.getByText(/1.*20.*of.*25/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    expect(screen.getByText(/21.*25.*of.*25/i)).toBeInTheDocument();
  });

  it('offers no page controls when everything fits on one page', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(3) } }));

    await screen.findByRole('table', { name: /edjers/i });

    expect(screen.queryByRole('navigation', { name: /pagination/i })).not.toBeInTheDocument();
    // The page-size control stays, so an administrator can still narrow the page.
    expect(screen.getByRole('combobox', { name: /per page/i })).toBeInTheDocument();
  });

  it('returns to the first page when the search changes', async () => {
    // Staying on page 2 of a result set the search has just replaced shows an apparently empty list, which
    // reads as "nothing matched" when the matches are one page back.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(25) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(screen.getByRole('cell', { name: 'Edjer021' })).toBeInTheDocument();

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'Edjer0');

    expect(screen.getByRole('cell', { name: 'Edjer001' })).toBeInTheDocument();
  });

  it('returns to the first page when the page size changes', async () => {
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(130) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(screen.getByRole('cell', { name: 'Edjer021' })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '50');

    expect(screen.getByRole('cell', { name: 'Edjer001' })).toBeInTheDocument();
  });

  it('paginates the FILTERED set, not the whole one', async () => {
    // 130 rows, of which 10 match — so the page must report 10, not 130, and must offer no page controls.
    // Counting against the unfiltered collection is the defect: it would page through 130 while showing a
    // handful of matches per page, most of them empty.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(130) } }));

    await screen.findByRole('table', { name: /edjers/i });

    // "edjer01" matches Edjer010-Edjer019 only — NOT Edjer001 ("edjer001" has no "edjer01" substring) and
    // not Edjer101 ("edjer101" contains "edjer10"). Ten rows.
    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'edjer01');

    expect(screen.getAllByRole('link', { name: /^First\d{3}$/ })).toHaveLength(10);
    expect(screen.getByText(/showing 1 to 10 of 10/i)).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: /pagination/i })).not.toBeInTheDocument();
  });

  it('never shows an empty page after a search narrows the results', async () => {
    // The clamp. Sitting on page 3 and then filtering down to 5 rows must land on a page that HAS rows,
    // rather than rendering a header with nothing under it.
    renderPage(stubFetchByUrl({ [`GET ${EDJERS}`]: { status: 200, body: manyEdjers(130) } }));

    await screen.findByRole('table', { name: /edjers/i });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /per page/i }), '20');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    await userEvent.type(screen.getByRole('searchbox', { name: /search/i }), 'edjer005');

    expect(screen.getAllByRole('link', { name: /^First\d{3}$/ })).toHaveLength(1);
    expect(screen.getByRole('cell', { name: 'Edjer005' })).toBeInTheDocument();
  });
});
