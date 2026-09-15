import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const currentUser = vi.hoisted(() => ({ value: { privileges: ['Compass Admin'] } as unknown }));
vi.mock('../../../../src/hooks/useCurrentUser', () => ({
  useCurrentUser: () => ({ data: currentUser.value }),
  currentUserQueryKey: ['current-user'],
}));

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { TeamDirectoryRoute } from '../../../../src/features/team-directory/TeamDirectoryRoute';

interface Row {
  id: number;
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  employeeType: string;
  coachId: number | null;
  coach: string | null;
  state: string;
  currentAssignments: never[];
}

function row(
  id: number,
  lastName: string,
  employeeType: string,
  state: string,
  coachId: number | null = 9,
  coach: string | null = 'Cody Coach',
): Row {
  return {
    id,
    firstName: 'Ada',
    lastName,
    hireDate: '2020-01-15',
    email: `${lastName.toLowerCase()}@example.test`,
    employeeType,
    coachId,
    coach,
    state,
    currentAssignments: [],
  };
}

/** The whole directory the fake server holds. */
const DIRECTORY = [
  row(1, 'Active', 'Full Time', 'OH'),
  row(2, 'Bright', 'Part Time', 'TX', 12, 'Robin Reports'),
  row(3, 'Carter', '1099', 'OH'),
];

/** The active skill list the public `/api/compass/skills` endpoint answers with. */
const SKILL_OPTIONS = [
  { id: 1, name: 'Java' },
  { id: 2, name: 'SQL' },
];

/** Answers like the real endpoint: applies the query's own filters, so a filtered call returns less. */
function respondFromDirectory(url: string) {
  const params = new URLSearchParams(url.slice(url.indexOf('?') + 1));
  const employeeType = params.get('employeeType');
  const state = params.get('state');
  const search = params.get('search');
  const coachId = params.get('coachId');

  const rows = DIRECTORY.filter(
    (candidate) =>
      (employeeType === null || candidate.employeeType === employeeType) &&
      (state === null || candidate.state === state) &&
      (search === null || candidate.lastName.toLowerCase().includes(search.toLowerCase())) &&
      (coachId === null || String(candidate.coachId) === coachId),
  );

  return { ok: true, status: 200, json: async () => rows };
}

/** The default fetch responder: the public skill list on its own endpoint, the directory otherwise. */
function respond(url: string) {
  return url.startsWith('/api/compass/skills')
    ? { ok: true, status: 200, json: async () => SKILL_OPTIONS }
    : respondFromDirectory(url);
}

function renderRoute() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <TeamDirectoryRoute />
    </QueryClientProvider>,
  );
}

/** Every URL the route has requested. */
function requestedUrls(): string[] {
  return fetchSpy.mock.calls.map((call) => String(call[0]));
}

describe('TeamDirectoryRoute', () => {
  beforeEach(() => {
    fetchSpy.mockReset();
    fetchSpy.mockImplementation((url: string) => Promise.resolve(respond(url)));
  });

  /** Every requested team-directory URL, ignoring the independent skill-options read. */
  function requestedDirectoryUrls(): string[] {
    return requestedUrls().filter((url) => url.includes('/api/compass/team-directory'));
  }

  it('requests the read surface with the default status filter', async () => {
    renderRoute();

    await waitFor(() => expect(requestedDirectoryUrls().length).toBeGreaterThan(0));
    expect(requestedDirectoryUrls()[0]).toContain('status=active');
  });

  it('opens with ONE team-directory request, not two', async () => {
    // The screen reads the directory twice — once for the table and once, unfiltered, for the count
    // pill and the filter options. On first load both are the same URL, and `useTeamDirectory` keys its
    // cache by that URL, so react-query serves them from a single request. Keying by the params OBJECT
    // instead would make `{status}` and `{search: '', status}` different keys for one URL, and the
    // screen would open by fetching the same rows twice.
    //
    // The independent skill-options read (its own endpoint, its own query key) is a legitimate separate
    // request and is excluded from this count via `requestedDirectoryUrls`.
    renderRoute();

    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());
    expect(requestedDirectoryUrls()).toHaveLength(1);
  });

  it('sends the search term to the SERVER rather than filtering locally', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());

    await user.type(screen.getByLabelText('Search by Last Name'), 'car');

    // FR-021: the browser must never hold rows the viewer is not entitled to, so there is exactly
    // one filtering model and it is the query.
    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('search=car'))).toBe(true),
    );
  });

  it('searches the whole directory, not the page on screen', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.type(screen.getByLabelText('Search by Last Name'), 'carter');

    await waitFor(() => expect(screen.getByRole('row', { name: /Carter/ })).toBeInTheDocument());
    expect(screen.queryByRole('row', { name: /Active/ })).not.toBeInTheDocument();
  });

  it('sends the employee-type filter to the server', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('Employee Type'), 'Part Time');

    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('employeeType=Part+Time'))).toBe(true),
    );
  });

  it('sends the state filter to the server', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('State'), 'TX');

    await waitFor(() => expect(requestedUrls().some((url) => url.includes('state=TX'))).toBe(true));
  });

  it('sends the coach filter to the server (issue #655)', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('Coach'), 'Robin Reports');

    await waitFor(() =>
      expect(requestedUrls().some((url) => url.includes('coachId=12'))).toBe(true),
    );
  });

  it('sends the skill filter to the server', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('Skill'), 'Java');

    await waitFor(() =>
      expect(requestedDirectoryUrls().some((url) => url.includes('skill=1'))).toBe(true),
    );
  });

  it('offers every skill from the public skill list', async () => {
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    const select = screen.getByLabelText('Skill');
    await waitFor(() =>
      expect(
        Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
      ).toEqual(['All Skills', 'Java', 'SQL']),
    );
  });

  it('narrows the table to just that coach’s team', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('Coach'), 'Robin Reports');

    await waitFor(() => expect(screen.getByRole('row', { name: /Bright/ })).toBeInTheDocument());
    expect(screen.queryByRole('row', { name: /Active/ })).not.toBeInTheDocument();
  });

  it('offers every coach in the directory, from the UNFILTERED read', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    const select = screen.getByLabelText('Coach');
    await user.selectOptions(select, 'Robin Reports');
    await waitFor(() => expect(screen.queryByRole('row', { name: /Active/ })).toBeNull());

    expect(
      Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
    ).toEqual(['All Coaches', 'Cody Coach', 'Robin Reports']);
  });

  it('offers every employee type in the directory, from the UNFILTERED read', async () => {
    // The trap this exists for: deriving the options from the rows on screen collapses the list to the
    // one option already chosen, and there is then no way back to "All".
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    const select = screen.getByLabelText('Employee Type');
    await user.selectOptions(select, 'Part Time');
    await waitFor(() => expect(screen.queryByRole('row', { name: /Active/ })).toBeNull());

    expect(
      Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
    ).toEqual(['All Employee Types', '1099', 'Full Time', 'Part Time']);
  });

  it('offers every state in the directory, named in full and de-duplicated', async () => {
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    expect(
      Array.from(screen.getByLabelText('State').querySelectorAll('option')).map(
        (option) => option.textContent,
      ),
    ).toEqual(['All States', 'Ohio', 'Texas']);
  });

  it('counts the whole scope in the pill, not the filtered table', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByText('3 active EDJErs')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('Employee Type'), 'Part Time');
    await waitFor(() => expect(screen.queryByRole('row', { name: /Active/ })).toBeNull());

    expect(screen.getByText('3 active EDJErs')).toBeInTheDocument();
  });

  it('breaks the count down by employee type, ordered Full Time → Part Time → 1099 → Intern → Unknown (#624), not alphabetically', async () => {
    renderRoute();

    await waitFor(() =>
      expect(screen.getByText('Full Time: 1 · Part Time: 1 · 1099: 1')).toBeInTheDocument(),
    );
  });

  it('buckets a blank employee type as "Unknown" so the breakdown still sums to the pill\'s count', async () => {
    // A row with no resolvable employee type (a TPS-carryover gap) must not just vanish from the
    // breakdown — `distinct()` filters out the blank value entirely, so without an explicit bucket
    // the listed counts would sum to less than the pill's own count with no way to see why.
    fetchSpy.mockImplementation((url: string) => {
      const params = new URLSearchParams(url.slice(url.indexOf('?') + 1));
      const rows =
        params.get('employeeType') === null
          ? [...DIRECTORY, row(4, 'Doyle', '', 'OH')]
          : DIRECTORY.filter((candidate) => candidate.employeeType === params.get('employeeType'));
      return Promise.resolve({ ok: true, status: 200, json: async () => rows });
    });

    renderRoute();

    await waitFor(() =>
      expect(
        screen.getByText('Full Time: 1 · Part Time: 1 · 1099: 1 · Unknown: 1'),
      ).toBeInTheDocument(),
    );
  });

  it('places Intern between 1099 and Unknown, not alphabetically before Full Time (#624)', async () => {
    fetchSpy.mockImplementation((url: string) => {
      const params = new URLSearchParams(url.slice(url.indexOf('?') + 1));
      const rows =
        params.get('employeeType') === null
          ? [...DIRECTORY, row(4, 'Doyle', 'Intern', 'OH'), row(5, 'Estes', '', 'OH')]
          : DIRECTORY.filter((candidate) => candidate.employeeType === params.get('employeeType'));
      return Promise.resolve({ ok: true, status: 200, json: async () => rows });
    });

    renderRoute();

    await waitFor(() =>
      expect(
        screen.getByText('Full Time: 1 · Part Time: 1 · 1099: 1 · Intern: 1 · Unknown: 1'),
      ).toBeInTheDocument(),
    );
  });

  it('sorts a type outside the known order after every named type and before Unknown', async () => {
    // Compass's `employee_type` lookup is runtime-editable by a Compass Super Admin, so a type this
    // constant has never heard of is a real possibility, not a hypothetical.
    fetchSpy.mockImplementation((url: string) => {
      const params = new URLSearchParams(url.slice(url.indexOf('?') + 1));
      const rows =
        params.get('employeeType') === null
          ? [...DIRECTORY, row(4, 'Doyle', 'Contractor', 'OH')]
          : DIRECTORY.filter((candidate) => candidate.employeeType === params.get('employeeType'));
      return Promise.resolve({ ok: true, status: 200, json: async () => rows });
    });

    renderRoute();

    await waitFor(() =>
      expect(
        screen.getByText('Full Time: 1 · Part Time: 1 · 1099: 1 · Contractor: 1'),
      ).toBeInTheDocument(),
    );
  });

  it('breaks a tie between two unknown types alphabetically', async () => {
    fetchSpy.mockImplementation((url: string) => {
      const params = new URLSearchParams(url.slice(url.indexOf('?') + 1));
      const rows =
        params.get('employeeType') === null
          ? [...DIRECTORY, row(4, 'Doyle', 'Zeta Type', 'OH'), row(5, 'Estes', 'Consultant', 'OH')]
          : DIRECTORY.filter((candidate) => candidate.employeeType === params.get('employeeType'));
      return Promise.resolve({ ok: true, status: 200, json: async () => rows });
    });

    renderRoute();

    await waitFor(() =>
      expect(
        screen.getByText('Full Time: 1 · Part Time: 1 · 1099: 1 · Consultant: 1 · Zeta Type: 1'),
      ).toBeInTheDocument(),
    );
  });

  it('re-reads the scope when the status filter changes', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText('Status'), 'all');

    // Exactly once between the two reads, not once each: at this point both are asking for the same
    // unfiltered URL again, and they share the cache entry.
    await waitFor(() =>
      expect(
        requestedUrls().filter((url) => url === '/api/compass/team-directory?status=all'),
      ).toHaveLength(1),
    );
  });

  it('keeps the table on screen while a new query is in flight', async () => {
    // Without placeholderData, changing the sort changes the query key, isPending flips true and the
    // table unmounts — the directory blanks and reflows on every keystroke.
    const user = userEvent.setup();
    renderRoute();

    const sortByLastName = await screen.findByRole('button', { name: 'Sort by last name' });
    await user.click(sortByLastName);

    expect(screen.getByRole('table', { name: /team directory/i })).toBeInTheDocument();
  });

  it('flips sort direction when the same column is chosen twice', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(fetchSpy).toHaveBeenCalled());

    const sortByLastName = await screen.findByRole('button', { name: 'Sort by last name' });

    await user.click(sortByLastName);
    await waitFor(
      () => expect(requestedUrls().some((url) => url.includes('sort=lastName'))).toBe(true),
      { timeout: 3000 },
    );

    await user.click(sortByLastName);
    await waitFor(
      () => expect(requestedUrls().some((url) => url.includes('desc=true'))).toBe(true),
      {
        timeout: 3000,
      },
    );
  });

  it('marks the column it is sorting by, so the header agrees with the request', async () => {
    const user = userEvent.setup();
    renderRoute();
    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Sort by last name' }));

    await waitFor(() =>
      expect(screen.getByRole('columnheader', { name: 'Last Name' })).toHaveAttribute(
        'aria-sort',
        'ascending',
      ),
    );
  });

  it('treats an unresolved session as holding no privileges', async () => {
    // /api/me has not answered yet, or the caller has no privileges array. Either way the status
    // control must not appear — defaulting to "some privileges" would offer a control that resolves
    // to nothing.
    currentUser.value = undefined;
    try {
      renderRoute();
      await waitFor(() => expect(fetchSpy).toHaveBeenCalled());

      expect(screen.queryByLabelText('Status')).not.toBeInTheDocument();
    } finally {
      currentUser.value = { privileges: ['Compass Admin'] };
    }
  });

  it('surfaces a failed request as an error state', async () => {
    fetchSpy.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    renderRoute();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('still renders the table when the scope read fails', async () => {
    // The scope read only feeds the count and the option lists. Losing it must not take the directory
    // with it, so the count falls back to the rows in hand.
    let call = 0;
    fetchSpy.mockImplementation((url: string) => {
      call += 1;
      return call === 1
        ? Promise.resolve(respondFromDirectory(url))
        : Promise.resolve({ ok: false, status: 500, json: async () => ({}) });
    });

    renderRoute();

    await waitFor(() => expect(screen.getByRole('table')).toBeInTheDocument());
    expect(screen.getByText('3 active EDJErs')).toBeInTheDocument();
  });
});
