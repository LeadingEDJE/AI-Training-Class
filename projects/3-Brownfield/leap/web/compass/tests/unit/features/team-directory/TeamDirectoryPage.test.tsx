import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { TeamDirectoryPage } from '../../../../src/features/team-directory/TeamDirectoryPage';
import type { TeamDirectoryRow } from '../../../../src/features/team-directory/types';

const ROWS: TeamDirectoryRow[] = [
  {
    id: 1,
    firstName: 'Ada',
    lastName: 'Active',
    hireDate: '2020-01-15',
    email: 'ada.active@example.test',
    employeeType: 'Full Time',
    coachId: 9,
    coach: 'Cody Coach',
    state: 'OH',
    currentAssignments: [{ clientId: 1, clientName: 'Client One' }],
  },
  {
    id: 2,
    firstName: 'Mo',
    lastName: 'Multi',
    hireDate: '2021-02-01',
    email: 'mo.multi@example.test',
    employeeType: 'Full Time',
    coachId: 9,
    coach: 'Cody Coach',
    state: 'OH',
    currentAssignments: [
      { clientId: 1, clientName: 'Client One' },
      { clientId: 2, clientName: 'Client Two' },
    ],
  },
  {
    id: 3,
    firstName: 'Ned',
    lastName: 'Nocoach',
    hireDate: '2017-08-01',
    email: 'ned.nocoach@example.test',
    employeeType: 'Contractor',
    coachId: null,
    coach: null,
    state: 'TX',
    currentAssignments: [],
  },
];

/** Enough rows to need more than the default page. */
function manyRows(count: number): TeamDirectoryRow[] {
  return Array.from({ length: count }, (_, index) => ({
    ...ROWS[0],
    id: 100 + index,
    lastName: `Surname${String(index).padStart(2, '0')}`,
    email: `edjer${index}@example.test`,
  }));
}

type PageProps = Parameters<typeof TeamDirectoryPage>[0];

function renderPage(overrides: Partial<PageProps> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <TeamDirectoryPage
        rows={ROWS}
        isPending={false}
        isError={false}
        privileges={[]}
        {...overrides}
      />
    </QueryClientProvider>,
  );
}

describe('TeamDirectoryPage', () => {
  it('renders a table with the AC-5 columns, labelled as the mockups label them', () => {
    renderPage();

    const table = screen.getByRole('table', { name: /team directory/i });
    const headers = within(table)
      .getAllByRole('columnheader')
      // The caret on the sorted column is an aria-hidden indicator, not part of the label. The
      // direction it stands for is asserted through `aria-sort` further down.
      .map((h) => h.textContent?.replace(/[▲▼]/g, '').trim());

    // No 'Email', and no trailing 'Actions' column either — the drill-in is the EDJEr's own name
    // (2026-08-19). Asserted as an exact array rather than a set of `toContain`s so a column silently
    // REAPPEARING fails too.
    expect(headers).toEqual([
      'First Name',
      'Last Name',
      'Hire Date',
      'Type',
      'Coach',
      'State',
      'Current Client(s)',
      'Skills',
    ]);
  });

  it('names the page as a heading', () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Team Directory', level: 1 })).toBeInTheDocument();
  });

  it('counts the whole status scope beside the title, not the filtered page', () => {
    // The mockups' "87 active EDJErs" pill. It reports the DIRECTORY, so it must not move when a
    // search narrows the table — otherwise it reads as the directory having shrunk.
    renderPage({ rows: ROWS, scopeCount: 87 });

    expect(screen.getByText('87 active EDJErs')).toBeInTheDocument();
  });

  it('says which status the count refers to when an elevated viewer changes scope', async () => {
    renderPage({ privileges: ['Compass Admin'], scopeCount: 4 });

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'inactive');

    expect(screen.getByText('4 former EDJErs')).toBeInTheDocument();
  });

  it('falls back to the rows in hand when no scope count was given', () => {
    renderPage();

    expect(screen.getByText('3 active EDJErs')).toBeInTheDocument();
  });

  it('says "EDJEr" in the singular', () => {
    renderPage({ rows: [ROWS[0]] });

    expect(screen.getByText('1 active EDJEr')).toBeInTheDocument();
  });

  it('breaks the scope count down by employee type, TPS carryover (#244), with no redundant total (#378)', () => {
    // The count pill beside the title already reports the scope ("76 active EDJErs"), so the
    // breakdown line below it lists only the per-type shares, not a second "Total: 76".
    renderPage({
      scopeCount: 76,
      typeCounts: [
        { type: '1099', count: 4 },
        { type: 'Full Time', count: 69 },
        { type: 'Part Time', count: 3 },
      ],
    });

    expect(screen.getByText('1099: 4 · Full Time: 69 · Part Time: 3')).toBeInTheDocument();
    expect(screen.queryByText(/Total:/)).not.toBeInTheDocument();
  });

  it('shows no breakdown before the scope read resolves', () => {
    renderPage({ typeCounts: [] });

    expect(screen.queryByText(/·/)).not.toBeInTheDocument();
  });

  it('renders a single type with no separator and no total (#378)', () => {
    renderPage({ rows: [ROWS[0]], typeCounts: [{ type: 'Full Time', count: 1 }] });

    expect(screen.getByText('Full Time: 1')).toBeInTheDocument();
  });

  it('renders the hire date as mm/dd/yyyy, as the mockups do', () => {
    renderPage();

    const row = screen.getByRole('row', { name: /Active/ });
    expect(within(row).getByText('01/15/2020')).toBeInTheDocument();
  });

  it('gives the hire date tabular figures, so the column cannot reflow as dates change', () => {
    // Load-bearing, not decoration, which is why it is pinned.
    //
    // The table is auto-layout, so this column's width is derived from its widest value. With
    // Manrope's default proportional figures, changing 08/14/2017 to 08/17/2017 changes that width
    // and the browser re-apportions EVERY column to the right of it. Tabular figures make the width
    // independent of the value, so the columns stop moving as the seeded dates roll forward —
    // `CompassDirectorySeeder.GeneratedHireDate` seeds `today - tenure`, so the rendered dates move
    // with the calendar.
    //
    // **The measurement that produced this is now historical.** It was found by the Compass visual
    // gate: a 1px narrower date shifted the column beside it 2px, mismatching five columns of text
    // and failing at 5% of all pixels against a 1% tolerance. Issue #574 deleted that gate — and
    // deleted `team-directory.visual.spec.ts`, which masked this cell using this very class as its
    // selector — so this unit test is now the ONLY thing holding the class. That is why it stays.
    renderPage();

    const row = screen.getByRole('row', { name: /Active/ });
    const dateCell = within(row).getByText('01/15/2020');

    expect(dateCell.className).toMatch(/(?:^|\s)tabular-nums(?:\s|$)/);
  });

  it('renders the employee type as the mockups’ pill', () => {
    renderPage();

    const row = screen.getByRole('row', { name: /Nocoach/ });
    expect(within(row).getByText('Contractor')).toBeInTheDocument();
  });

  it('reaches the employee detail through the EDJEr’s first name, not the email (AC-7)', () => {
    // The mockup made the email cell the drill-in; the owner replaced it with the EDJEr's own name
    // (2026-08-19, superseding the trailing View action of 2026-08-18) — the same mechanism
    // `ClientDirectoryPage` uses for a client's name.
    renderPage();

    expect(screen.getByRole('link', { name: 'Ada' })).toHaveAttribute(
      'href',
      '/compass/team-directory/1',
    );
  });

  it('names the drill-in with its own visible text, never an aria-label (WCAG 2.5.3)', () => {
    // **Label in Name.** The accessible name must CONTAIN the visible text, or a voice-control user
    // saying what they see cannot activate the control. The strongest form of that is no override at
    // all: the link's accessible name simply IS what is rendered.
    //
    // Asserted here rather than in a browser (`docs/TEST-STRATEGY.md` Rule 3) — nothing about it needs
    // a layout engine, an API or a real origin. It replaces the equivalent assertion in the deleted
    // `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts`, and is stricter than the
    // `getByRole('link', { name: 'Ada' })` queries above, which would still match an `aria-label="Ada"`.
    renderPage();

    expect(screen.getByRole('link', { name: 'Ada' })).not.toHaveAttribute('aria-label');
    expect(screen.getByRole('link', { name: 'Active' })).not.toHaveAttribute('aria-label');
  });

  it('reaches the employee detail through the EDJEr’s last name too', () => {
    // Both cells link, not just one — "first name" and "last name" are separate `<td>`s, and a
    // viewer who clicks the surname should reach the same place as one who clicks the given name.
    renderPage();

    expect(screen.getByRole('link', { name: 'Active' })).toHaveAttribute(
      'href',
      '/compass/team-directory/1',
    );
  });

  it('gives every link in the table the one shared table-link style', () => {
    // This screen's name link WAS the run every other table now shares (owner request 2026-08-21).
    // Asserted against the helper, not a class list, so the two cannot drift.
    renderPage();

    const multi = screen.getByRole('row', { name: /Multi/ });
    expect(screen.getByRole('link', { name: 'Ada' }).className).toBe(tableLinkClass);
    expect(screen.getByRole('link', { name: 'Active' }).className).toBe(tableLinkClass);
    expect(within(multi).getByRole('link', { name: 'Client One' }).className).toBe(tableLinkClass);
  });

  it('shows no email address in the directory at all', () => {
    // The address is still on the wire and still on the EDJEr's own record — the requirement is that
    // the LISTING does not show it, so this asserts absence rather than the header list above.
    renderPage();

    expect(screen.queryByText('ada.active@example.test')).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: /email/i })).not.toBeInTheDocument();
  });

  it('renders the assigned skill names in the Skills column', () => {
    renderPage({
      rows: [
        {
          ...ROWS[0],
          skills: [
            { id: 1, name: 'Java' },
            { id: 2, name: 'SQL' },
          ],
        },
      ],
    });

    const row = screen.getByRole('row', { name: /Active/ });
    expect(within(row).getByText('Java, SQL')).toBeInTheDocument();
  });

  it('renders an empty Skills cell when an EDJEr has none', () => {
    renderPage({ rows: [ROWS[0]] });

    const row = screen.getByRole('row', { name: /Active/ });
    expect(within(row).getAllByRole('cell').at(-1)).toHaveTextContent('');
  });

  it('renders every current assignment as its own client link, not just the first (AC-5)', () => {
    renderPage();

    const row = screen.getByRole('row', { name: /Multi/ });
    expect(within(row).getByRole('link', { name: 'Client One' })).toHaveAttribute(
      'href',
      '/compass/client-directory/1',
    );
    expect(within(row).getByRole('link', { name: 'Client Two' })).toHaveAttribute(
      'href',
      '/compass/client-directory/2',
    );
  });

  it('keeps the row for an EDJEr with no assignment', () => {
    renderPage();

    const row = screen.getByRole('row', { name: /Nocoach/ });
    expect(within(row).queryAllByRole('link', { name: /Client/ })).toHaveLength(0);
  });

  it('renders an empty coach cell rather than dropping the row', () => {
    renderPage();

    expect(screen.getByRole('row', { name: /Nocoach/ })).toBeInTheDocument();
  });

  it('links the coach cell into the coach’s own record', () => {
    renderPage();

    const row = screen.getByRole('row', { name: /Active/ });
    expect(within(row).getByRole('link', { name: 'Cody Coach' })).toHaveAttribute(
      'href',
      '/compass/team-directory/9',
    );
  });

  it('renders no coach link at all for an EDJEr with no coach', () => {
    renderPage();

    const row = screen.getByRole('row', { name: /Nocoach/ });
    const coachCell = within(row).getAllByRole('cell')[4];
    expect(within(coachCell).queryByRole('link')).not.toBeInTheDocument();
  });

  it('marks the sorted column with a direction, defaulting to the server’s hire date order', () => {
    renderPage();

    const table = screen.getByRole('table', { name: /team directory/i });
    expect(within(table).getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
    expect(within(table).getByRole('columnheader', { name: 'Last Name' })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('marks the sorted column descending when the server is sorting that way', () => {
    renderPage({ sort: 'lastName', descending: true });

    const table = screen.getByRole('table', { name: /team directory/i });
    expect(within(table).getByRole('columnheader', { name: 'Last Name' })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
    expect(within(table).getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
      'aria-sort',
      'none',
    );
  });

  it('offers no status filter to a baseline viewer (FR-011a)', () => {
    // The control is an elevated convenience. Its absence is not the access control — that is
    // enforced server-side — but showing it to someone entitled to nothing would be a dead end.
    renderPage({ privileges: [] });

    expect(screen.queryByLabelText('Status')).not.toBeInTheDocument();
  });

  it('offers a status filter to an elevated viewer, preset to Active (Q2)', () => {
    renderPage({ privileges: ['Compass Admin'] });

    expect(screen.getByLabelText('Status')).toHaveValue('active');
  });

  it('ignores Timesheet roles when deciding whether to offer the status filter', () => {
    // Compass inherits nothing, not even root — and DevBypass carries all nine timesheet strings.
    renderPage({ privileges: ['SuperAdmin', 'Admin', 'Ops'] });

    expect(screen.queryByLabelText('Status')).not.toBeInTheDocument();
  });

  it('shows an explicit empty state rather than a blank table (FR-013)', () => {
    renderPage({ rows: [] });

    expect(screen.getByText(/no edjers match/i)).toBeInTheDocument();
  });

  it('shows a loading state', () => {
    renderPage({ rows: [], isPending: true });

    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows an error state', () => {
    renderPage({ rows: [], isError: true });

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('reports a status change to its caller', async () => {
    const onStatusChange = vi.fn();
    renderPage({ privileges: ['Compass Admin'], onStatusChange });

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'all');

    expect(onStatusChange).toHaveBeenCalledWith('all');
  });

  it('reports search and sort changes to its caller', async () => {
    const onSearchChange = vi.fn();
    const onSortChange = vi.fn();
    renderPage({ onSearchChange, onSortChange });

    await userEvent.type(screen.getByLabelText('Search by Last Name'), 'a');
    await userEvent.click(screen.getByRole('button', { name: 'Sort by state' }));

    expect(onSearchChange).toHaveBeenCalledWith('a');
    expect(onSortChange).toHaveBeenCalledWith('state');
  });

  it('lets the viewer sort by current client (TD-3, feature 008 FR-010)', async () => {
    // The mockup's note says "every column header sorts (TD-3)" and Journey Map v6 J2 step 4 says
    // "EDJEr sorts by any column". This column was the one exception.
    //
    // The `currentClients` key must match `CompassReadRepository.Sort`'s branch exactly — sorting is
    // SERVER-side, so a key this page invents and the server does not recognise silently falls through
    // to hire date and looks like it worked.
    const onSortChange = vi.fn();
    renderPage({ onSortChange });

    await userEvent.click(screen.getByRole('button', { name: 'Sort by current client(s)' }));

    expect(onSortChange).toHaveBeenCalledWith('currentClients');
  });

  it('marks the current-client column as sorted when it is the active sort', () => {
    renderPage({ sort: 'currentClients' });

    const table = screen.getByRole('table', { name: /team directory/i });
    expect(within(table).getByRole('columnheader', { name: 'Current Client(s)' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
  });

  it('renders a Former badge so an inactive EDJEr is distinguishable (AC-15)', () => {
    // Text, not colour alone — the status has to survive a greyscale render and a screen reader.
    renderPage({
      rows: [
        { ...ROWS[0], isActive: false },
        { ...ROWS[1], isActive: true },
      ],
      privileges: ['Compass Admin'],
    });

    // Scoped to the table: the elevated viewer's status filter also offers a "Former" option, so an
    // unscoped query matches the control as well as the badge.
    const table = screen.getByRole('table', { name: /team directory/i });
    expect(within(table).getByText('Former')).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /Multi/ })).queryByText('Former')).toBeNull();
  });

  it('tolerates callbacks being absent', async () => {
    // The page is usable standalone; the optional handlers must short-circuit rather than throw.
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: 'Sort by state' }));

    expect(screen.getByRole('table', { name: /team directory/i })).toBeInTheDocument();
  });

  it('lets the viewer type a search term', async () => {
    renderPage();

    const search = screen.getByLabelText('Search by Last Name');
    await userEvent.type(search, 'mul');

    await waitFor(() => expect(search).toHaveValue('mul'));
  });

  describe('the AC-6 filters', () => {
    it('offers every employee type it was given, plus an all-types default', async () => {
      const onEmployeeTypeChange = vi.fn();
      renderPage({
        employeeTypeOptions: ['1099', 'Full Time', 'Part Time'],
        onEmployeeTypeChange,
      });

      const select = screen.getByLabelText('Employee Type');
      expect(
        Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
      ).toEqual(['All Employee Types', '1099', 'Full Time', 'Part Time']);

      await userEvent.selectOptions(select, 'Part Time');
      expect(onEmployeeTypeChange).toHaveBeenCalledWith('Part Time');
    });

    it('names each state in full while sending its code, so the option is readable', async () => {
      const onStateChange = vi.fn();
      renderPage({ stateOptions: ['OH', 'TX'], onStateChange });

      const select = screen.getByLabelText('State');
      expect(
        Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
      ).toEqual(['All States', 'Ohio', 'Texas']);

      await userEvent.selectOptions(select, 'TX');
      expect(onStateChange).toHaveBeenCalledWith('TX');
    });

    it('falls back to the bare code for a state it cannot name', () => {
      // Presentation must not swallow a value the server sent. `UsStateCodes.cs` is the authority and
      // `us-states.ts` is its presentation half; a drift between them should show as an odd-looking
      // option, never as a missing one.
      renderPage({ stateOptions: ['ZZ'] });

      expect(
        Array.from(screen.getByLabelText('State').querySelectorAll('option')).map(
          (option) => option.textContent,
        ),
      ).toEqual(['All States', 'ZZ']);
    });

    it('reports clearing a filter as an empty value, meaning "no constraint"', async () => {
      const onEmployeeTypeChange = vi.fn();
      renderPage({ employeeTypeOptions: ['Full Time'], onEmployeeTypeChange });

      const select = screen.getByLabelText('Employee Type');
      await userEvent.selectOptions(select, 'Full Time');
      await userEvent.selectOptions(select, '');

      expect(onEmployeeTypeChange).toHaveBeenLastCalledWith('');
    });

    it('offers every coach it was given, plus an all-coaches default (issue #655)', async () => {
      const onCoachIdChange = vi.fn();
      renderPage({
        coachOptions: [
          { id: 9, name: 'Cody Coach' },
          { id: 12, name: 'Robin Reports' },
        ],
        onCoachIdChange,
      });

      const select = screen.getByLabelText('Coach');
      expect(
        Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
      ).toEqual(['All Coaches', 'Cody Coach', 'Robin Reports']);

      await userEvent.selectOptions(select, 'Cody Coach');
      expect(onCoachIdChange).toHaveBeenCalledWith('9');
    });

    it('reports clearing the coach filter as an empty value', async () => {
      const onCoachIdChange = vi.fn();
      renderPage({ coachOptions: [{ id: 9, name: 'Cody Coach' }], onCoachIdChange });

      const select = screen.getByLabelText('Coach');
      await userEvent.selectOptions(select, 'Cody Coach');
      await userEvent.selectOptions(select, '');

      expect(onCoachIdChange).toHaveBeenLastCalledWith('');
    });

    it('offers every skill it was given, plus an all-skills default', async () => {
      const onSkillChange = vi.fn();
      renderPage({
        skillOptions: [
          { id: 1, name: 'Java' },
          { id: 2, name: 'SQL' },
        ],
        onSkillChange,
      });

      const select = screen.getByLabelText('Skill');
      expect(
        Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
      ).toEqual(['All Skills', 'Java', 'SQL']);

      await userEvent.selectOptions(select, 'Java');
      expect(onSkillChange).toHaveBeenCalledWith('1');
    });

    it('reports clearing the skill filter as an empty value', async () => {
      const onSkillChange = vi.fn();
      renderPage({ skillOptions: [{ id: 1, name: 'Java' }], onSkillChange });

      const select = screen.getByLabelText('Skill');
      await userEvent.selectOptions(select, 'Java');
      await userEvent.selectOptions(select, '');

      expect(onSkillChange).toHaveBeenLastCalledWith('');
    });
  });

  describe('pagination', () => {
    it('defaults to showing every row, not a paged subset (issue #247)', () => {
      renderPage({ rows: manyRows(25) });

      const table = screen.getByRole('table', { name: /team directory/i });
      // 25 rows plus the header row — the whole roster, unpaged.
      expect(within(table).getAllByRole('row')).toHaveLength(26);
      expect(screen.getByText(/Showing 1 to 25 of 25 EDJErs/)).toBeInTheDocument();
      expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
    });

    it('pages once a viewer picks a smaller size than the default', async () => {
      renderPage({ rows: manyRows(25) });

      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      const table = screen.getByRole('table', { name: /team directory/i });
      // 20 rows plus the header row.
      expect(within(table).getAllByRole('row')).toHaveLength(21);
      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });

    it('moves to the next page once paged', async () => {
      renderPage({ rows: manyRows(25) });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));

      expect(screen.getByText(/Showing 21 to 25 of 25 EDJErs/)).toBeInTheDocument();
      expect(screen.getByRole('row', { name: /Surname24/ })).toBeInTheDocument();
      expect(screen.queryByRole('row', { name: /Surname00/ })).not.toBeInTheDocument();
    });

    it('offers no page controls when everything fits on one page', () => {
      renderPage();

      expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
    });

    it('can return to showing every row after picking a smaller size', async () => {
      renderPage({ rows: manyRows(25) });

      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), 'all');

      const table = screen.getByRole('table', { name: /team directory/i });
      expect(within(table).getAllByRole('row')).toHaveLength(26);
      expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
    });

    it('searches ACROSS pages, not within the one on screen', async () => {
      // The whole point of pagination here: the term goes to the server, which searches the entire
      // directory, and the results come back as page 1 of whatever matched. A viewer on page 2 who
      // searches must not be shown "no matches" because nothing on page 2 matched.
      const onSearchChange = vi.fn();
      renderPage({ rows: manyRows(25), onSearchChange });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));
      expect(screen.getByText(/Showing 21 to 25 of 25 EDJErs/)).toBeInTheDocument();

      await userEvent.type(screen.getByLabelText('Search by Last Name'), 'sur');

      expect(onSearchChange).toHaveBeenLastCalledWith('sur');
      // Back to the first page of the NEW result set, not stranded on a page index from the old one.
      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });

    it('returns to the first page when a filter changes', async () => {
      renderPage({ rows: manyRows(25), employeeTypeOptions: ['Full Time'] });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));
      await userEvent.selectOptions(screen.getByLabelText('Employee Type'), 'Full Time');

      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });

    it('returns to the first page when the status scope changes', async () => {
      renderPage({ rows: manyRows(25), privileges: ['Compass Admin'] });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));
      await userEvent.selectOptions(screen.getByLabelText('Status'), 'all');

      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });

    it('returns to the first page when the state filter changes', async () => {
      renderPage({ rows: manyRows(25), stateOptions: ['OH'] });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));
      await userEvent.selectOptions(screen.getByLabelText('State'), 'OH');

      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });

    it('returns to the first page when the coach filter changes (issue #655)', async () => {
      renderPage({ rows: manyRows(25), coachOptions: [{ id: 9, name: 'Cody Coach' }] });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));
      await userEvent.selectOptions(screen.getByLabelText('Coach'), 'Cody Coach');

      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });

    it('offers neither a pager nor a page size when there is nothing to page', () => {
      renderPage({ rows: [] });

      expect(screen.queryByLabelText('EDJErs per Page')).not.toBeInTheDocument();
      expect(screen.queryByText(/Showing/)).not.toBeInTheDocument();
    });
  });

  describe('clearing all filters (#246)', () => {
    it('disables Reset View when the view already matches the default', () => {
      renderPage();

      expect(screen.getByRole('button', { name: 'Reset View' })).toBeDisabled();
    });

    it('enables Reset View once a filter differs from the default', async () => {
      renderPage();

      await userEvent.type(screen.getByLabelText('Search by Last Name'), 'a');

      expect(screen.getByRole('button', { name: 'Reset View' })).toBeEnabled();
    });

    it('clears the search box, employee type, coach and state filters, and reports it to the caller', async () => {
      const onSearchChange = vi.fn();
      const onEmployeeTypeChange = vi.fn();
      const onStateChange = vi.fn();
      const onCoachIdChange = vi.fn();
      renderPage({
        employeeTypeOptions: ['Full Time'],
        stateOptions: ['OH'],
        coachOptions: [{ id: 9, name: 'Cody Coach' }],
        onSearchChange,
        onEmployeeTypeChange,
        onStateChange,
        onCoachIdChange,
      });

      await userEvent.type(screen.getByLabelText('Search by Last Name'), 'mul');
      await userEvent.selectOptions(screen.getByLabelText('Employee Type'), 'Full Time');
      await userEvent.selectOptions(screen.getByLabelText('State'), 'OH');
      await userEvent.selectOptions(screen.getByLabelText('Coach'), 'Cody Coach');

      await userEvent.click(screen.getByRole('button', { name: 'Reset View' }));

      expect(screen.getByLabelText('Search by Last Name')).toHaveValue('');
      expect(screen.getByLabelText('Employee Type')).toHaveValue('');
      expect(screen.getByLabelText('State')).toHaveValue('');
      expect(screen.getByLabelText('Coach')).toHaveValue('');
      expect(onSearchChange).toHaveBeenLastCalledWith('');
      expect(onEmployeeTypeChange).toHaveBeenLastCalledWith('');
      expect(onStateChange).toHaveBeenLastCalledWith('');
      expect(onCoachIdChange).toHaveBeenLastCalledWith('');
    });

    it('resets an elevated viewer’s status filter back to Active, then disables itself', async () => {
      const onStatusChange = vi.fn();
      renderPage({ privileges: ['Compass Admin'], onStatusChange });

      await userEvent.selectOptions(screen.getByLabelText('Status'), 'all');
      expect(screen.getByRole('button', { name: 'Reset View' })).toBeEnabled();

      await userEvent.click(screen.getByRole('button', { name: 'Reset View' }));

      expect(screen.getByLabelText('Status')).toHaveValue('active');
      expect(onStatusChange).toHaveBeenLastCalledWith('active');
      expect(screen.getByRole('button', { name: 'Reset View' })).toBeDisabled();
    });

    it('leaves the column sort and page size alone (Option A)', async () => {
      const onSortChange = vi.fn();
      renderPage({
        rows: manyRows(25),
        sort: 'lastName',
        descending: true,
        onSortChange,
      });

      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');
      await userEvent.type(screen.getByLabelText('Search by Last Name'), 'sur');

      await userEvent.click(screen.getByRole('button', { name: 'Reset View' }));

      expect(onSortChange).not.toHaveBeenCalled();
      expect(screen.getByLabelText('EDJErs per Page')).toHaveValue('20');
    });

    it('returns to the first page when filters are cleared', async () => {
      renderPage({ rows: manyRows(25), employeeTypeOptions: ['Full Time'] });
      await userEvent.selectOptions(screen.getByLabelText('EDJErs per Page'), '20');

      await userEvent.selectOptions(screen.getByLabelText('Employee Type'), 'Full Time');
      await userEvent.click(screen.getByRole('button', { name: 'Next' }));

      await userEvent.click(screen.getByRole('button', { name: 'Reset View' }));

      expect(screen.getByText(/Showing 1 to 20 of 25 EDJErs/)).toBeInTheDocument();
    });
  });
});
