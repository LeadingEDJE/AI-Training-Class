import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buttonClassName, tableLinkClass } from '../../../../src/components/ui-classes';
import { EmployeeDetailPage } from '../../../../src/features/employee-detail/EmployeeDetailPage';
import type { EmployeeDetail } from '../../../../src/features/employee-detail/types';

/** What a BASELINE viewer receives for someone else's record: no notes, no SOWs, no settings. */
const BASELINE_VIEW: EmployeeDetail = {
  id: 2,
  firstName: 'Otto',
  lastName: 'Other',
  hireDate: '2021-02-01',
  email: 'otto.other@example.test',
  employeeType: 'Full Time',
  coachId: 9,
  coach: 'Cody Coach',
  state: 'OH',
  isDeliveryTeam: true,
  directReports: [],
  assignmentHistory: [
    {
      assignmentId: 1,
      clientId: 1,
      clientName: 'Client One',
      startDate: '2022-01-01',
      endDate: null,
    },
    {
      assignmentId: 2,
      clientId: 2,
      clientName: 'Client Two',
      startDate: '2022-01-01',
      endDate: '2023-06-30',
    },
  ],
};

/** What an ELEVATED viewer receives for the same record. */
const ELEVATED_VIEW: EmployeeDetail = {
  ...BASELINE_VIEW,
  isActive: true,
  assignmentHistory: [
    {
      assignmentId: 1,
      clientId: 1,
      clientName: 'Client One',
      startDate: '2022-01-01',
      endDate: null,
      note: 'Renewal discussion pending',
      sows: [
        {
          sowType: 'SowExtension',
          startDate: '2023-01-02',
          endDate: '2024-01-01',
          rateIncrease: true,
          note: 'Rate held at prior level',
        },
      ],
    },
  ],
};

function renderDetail(detail: EmployeeDetail, canManageAssignments = false) {
  return render(
    <EmployeeDetailPage
      detail={detail}
      isPending={false}
      isError={false}
      canManageAssignments={canManageAssignments}
    />,
  );
}

/**
 * The `<div>` holding one `<dt>`/`<dd>` pair in the profile list.
 *
 * Scoped rather than a bare `getByText('Yes')`: the time-tracking section renders Yes/No too, so an
 * unscoped match would pass against the wrong row.
 */
function profilePair(label: string): HTMLElement {
  const term = screen.getByText(label);
  if (term.parentElement === null) {
    throw new Error(`"${label}" is not inside a <dt>/<dd> pair.`);
  }
  return term.parentElement;
}

describe('EmployeeDetailPage', () => {
  it('shows the profile', () => {
    renderDetail(BASELINE_VIEW);

    expect(screen.getByRole('heading', { name: 'Otto Other' })).toBeInTheDocument();
    expect(screen.getByText('Full Time')).toBeInTheDocument();
    expect(screen.getByText('Cody Coach')).toBeInTheDocument();
  });

  it('does not display the EDJEr’s email anywhere on the page (issue #245)', () => {
    // Still on the wire (see `EmployeeDetail.email`) — this asserts the LISTING doesn't show it,
    // same distinction the Team Directory's own email-removal test draws.
    renderDetail(BASELINE_VIEW);

    expect(screen.queryByText('otto.other@example.test')).not.toBeInTheDocument();
    expect(screen.queryByText(/^email$/i)).not.toBeInTheDocument();
  });

  it('links the coach’s name to their own detail record (issue #245)', () => {
    // The Team Directory issue that started this: a name alone cannot be a link without an id to
    // link to, which is exactly what `coachId` carries.
    renderDetail(BASELINE_VIEW);

    expect(screen.getByRole('link', { name: 'Cody Coach' })).toHaveAttribute(
      'href',
      '/compass/team-directory/9',
    );
  });

  it('gives its links the one shared table-link style, in the table and beside it', () => {
    // Owner request 2026-08-21. The table is the scope of the request, but the coach link two panels
    // up carried the SAME run on the SAME screen — leaving it would ship one page with two styles.
    renderDetail(BASELINE_VIEW);

    expect(screen.getByRole('link', { name: 'Cody Coach' }).className).toBe(tableLinkClass);
    expect(screen.getByRole('link', { name: 'Client One' }).className).toBe(tableLinkClass);
  });

  it('formats the hire date as mm/dd/yyyy rather than the raw yyyy-mm-dd wire shape (#234)', () => {
    renderDetail(BASELINE_VIEW);

    expect(screen.getByText('02/01/2021')).toBeInTheDocument();
  });

  it('offers NO edit control to any viewer (AC-8)', () => {
    // The Super Admin's edit path is Stream 2's configuration surface, reached on its own terms.
    renderDetail(ELEVATED_VIEW);

    expect(
      screen.queryByRole('button', { name: /edit|save|update|delete/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('links each client in the assignment history to its client view (AC-8)', () => {
    renderDetail(BASELINE_VIEW);

    expect(screen.getByRole('link', { name: 'Client One' })).toHaveAttribute(
      'href',
      '/compass/client-directory/1',
    );
  });

  describe('"New assignment" action (AC-1, AC-2, feature 006)', () => {
    it('is hidden by default (AC-44 — a usability affordance, not the authorization boundary)', () => {
      renderDetail(BASELINE_VIEW);

      expect(screen.queryByRole('link', { name: /new assignment/i })).not.toBeInTheDocument();
    });

    it("appears when the viewer can manage assignments, linking to this EDJEr's new-assignment route", () => {
      renderDetail(BASELINE_VIEW, true);

      expect(screen.getByRole('link', { name: 'New Assignment' })).toHaveAttribute(
        'href',
        '/compass/team-directory/2/assignments/new',
      );
    });

    it('wears the primary button appearance at the nested size', () => {
      // Owner report 2026-08-18: it shipped as `bg-brand-green/15`, visibly paler than every other
      // primary action in Compass. Asserted against `buttonClassName` itself rather than a class list,
      // so the two cannot drift again — which is the reason feature 008 exported the helper for a link
      // to wear.
      //
      // `size: 'small'` per the rule on the helper (#336): this sits inside a panel, not in the page
      // header, and it was one of the three sites that had the header size. The equivalent control on
      // `ClientAssignmentHistory` was already small — that is the disagreement being closed.
      renderDetail(BASELINE_VIEW, true);

      expect(screen.getByRole('link', { name: 'New Assignment' }).className).toContain(
        buttonClassName({ variant: 'primary', size: 'small' }),
      );
    });
  });

  describe('"View assignment" affordance (AC-16/FR-025)', () => {
    it('is withheld when the server did not grant it — an entitlement, not a client-side conditional', () => {
      // BASELINE_VIEW's rows carry no `canViewAssignment`, matching what a baseline viewer's payload
      // actually looks like: the server omits the field rather than sending it false.
      renderDetail(BASELINE_VIEW);

      expect(screen.queryByRole('link', { name: /view assignment/i })).not.toBeInTheDocument();
    });

    it('links each row into its own assignment detail screen, nested under this EDJEr (AC-1, AC-2)', () => {
      const granted: EmployeeDetail = {
        ...BASELINE_VIEW,
        assignmentHistory: BASELINE_VIEW.assignmentHistory.map((a) => ({
          ...a,
          canViewAssignment: true,
        })),
      };
      renderDetail(granted);

      const links = screen.getAllByRole('link', { name: /view assignment/i });
      expect(links[0]).toHaveAttribute('href', '/compass/team-directory/2/assignments/1');
      expect(links[1]).toHaveAttribute('href', '/compass/team-directory/2/assignments/2');
    });
  });

  it('shows ended assignments as well as current ones', () => {
    renderDetail(BASELINE_VIEW);

    const history = screen.getByRole('table', { name: /assignment history/i });
    expect(within(history).getAllByRole('row').length).toBeGreaterThan(2);
    expect(within(history).getByText('06/30/2023')).toBeInTheDocument();
  });

  it('marks an open-ended assignment as current rather than showing a blank end date', () => {
    renderDetail(BASELINE_VIEW);

    const history = screen.getByRole('table', { name: /assignment history/i });
    expect(within(history).getByText(/current/i)).toBeInTheDocument();
  });

  it('shows delivery-team membership to a viewer who cannot see the time-tracking settings', () => {
    // FR-004 / AC-8. The payload here carries no timeTrackingSettings at all, which is what a
    // baseline viewer receives — so a value rendered inside that gated section would be invisible to
    // exactly the audience this criterion is about.
    renderDetail(BASELINE_VIEW);

    expect(screen.queryByText(/time tracking settings/i)).not.toBeInTheDocument();
    expect(within(profilePair('Delivery Team')).getByText('Yes')).toBeInTheDocument();
  });

  it('renders delivery-team membership in both states', () => {
    // Two branches, because a flag rendered inverted reads as entirely plausible. The affirmative
    // case is the test above; this is the other one.
    renderDetail({ ...BASELINE_VIEW, isDeliveryTeam: false });

    expect(within(profilePair('Delivery Team')).getByText('No')).toBeInTheDocument();
  });

  it('renders delivery-team membership as text, not as a control (AC-8)', () => {
    // The screen offers no edit path to any viewer. Compass's Toggle is role="switch", so a control
    // here would be reachable and operable even though nothing would save it.
    renderDetail(BASELINE_VIEW);

    // Anchored on the value first: two negatives alone also hold when the field renders nowhere,
    // so on their own they cannot tell "read-only" from "absent".
    expect(within(profilePair('Delivery Team')).getByText('Yes')).toBeInTheDocument();
    expect(screen.queryByRole('switch', { name: /delivery team/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /delivery team/i })).not.toBeInTheDocument();
  });

  it('omits the Time Tracking Settings section entirely when the server withheld it (AC-10)', () => {
    // Not "renders it blank" — the section must be ABSENT, or the viewer learns it exists.
    renderDetail(ELEVATED_VIEW);

    expect(screen.queryByText(/time tracking settings/i)).not.toBeInTheDocument();
  });

  it('shows Time Tracking Settings when the server sent them (AC-10)', () => {
    renderDetail({
      ...ELEVATED_VIEW,
      timeTrackingSettings: {
        timesheetRequired: true,
        canSubmitUnder40: false,
        includeInPayroll: true,
      },
    });

    expect(screen.getByText(/time tracking settings/i)).toBeInTheDocument();
    expect(screen.getByText(/timesheet required/i)).toBeInTheDocument();
  });

  it('renders each time-tracking flag in both states', () => {
    // Three independent booleans, each rendered as Yes/No. Asserting only one combination leaves the
    // opposite branch of every one of them unexercised — and a flag rendered inverted is exactly the
    // kind of defect that reads as plausible.
    renderDetail({
      ...ELEVATED_VIEW,
      timeTrackingSettings: {
        timesheetRequired: false,
        canSubmitUnder40: true,
        includeInPayroll: false,
      },
    });

    expect(screen.getByText(/timesheet required: no/i)).toBeInTheDocument();
    expect(screen.getByText(/can submit under 40: yes/i)).toBeInTheDocument();
    expect(screen.getByText(/include in payroll: no/i)).toBeInTheDocument();
  });

  it('omits SOWs, notes and the rate-increase indicator when the server withheld them (AC-11)', () => {
    renderDetail(BASELINE_VIEW);

    expect(screen.queryByText(/sow/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/rate increase/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/note/i)).not.toBeInTheDocument();
  });

  it('shows SOWs, notes and the rate-increase indicator when the server sent them (AC-11)', () => {
    renderDetail(ELEVATED_VIEW);

    expect(screen.getByText('Renewal discussion pending')).toBeInTheDocument();
    expect(screen.getByText('Rate held at prior level')).toBeInTheDocument();
    expect(screen.getByText(/rate increase/i)).toBeInTheDocument();
  });

  it('formats the SOW date range as mm/dd/yyyy rather than the raw yyyy-mm-dd wire shape (#234)', () => {
    renderDetail(ELEVATED_VIEW);

    expect(screen.getByText(/01\/02\/2023 to 01\/01\/2024/)).toBeInTheDocument();
  });

  it('shows a Former badge when the server reports an inactive EDJEr', () => {
    renderDetail({ ...ELEVATED_VIEW, isActive: false });

    expect(screen.getByText('Former')).toBeInTheDocument();
  });

  it('shows an empty history rather than an error when there is none', () => {
    renderDetail({ ...BASELINE_VIEW, assignmentHistory: [] });

    expect(screen.getByText(/no assignments/i)).toBeInTheDocument();
  });

  it('renders an em dash for an EDJEr with no coach', () => {
    // Coach is optional (AC-17); a blank cell reads as missing data rather than as "none".
    renderDetail({ ...BASELINE_VIEW, coachId: null, coach: null });

    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renders no coach link at all when there is no coach', () => {
    renderDetail({ ...BASELINE_VIEW, coachId: null, coach: null });

    expect(screen.queryByRole('link', { name: /coach/i })).not.toBeInTheDocument();
  });

  describe('Direct Reports (issue #245)', () => {
    it('omits the section entirely for an EDJEr who coaches no one', () => {
      // Most EDJErs are not coaches — showing an empty section on every record would be noise, so
      // this branches on presence the same way `timeTrackingSettings` does.
      renderDetail({ ...BASELINE_VIEW, directReports: [] });

      expect(screen.queryByRole('heading', { name: 'Direct Reports' })).not.toBeInTheDocument();
    });

    it('lists every direct report, each linking into their own record', () => {
      renderDetail({
        ...BASELINE_VIEW,
        directReports: [
          { id: 10, firstName: 'Dana', lastName: 'Direct' },
          { id: 11, firstName: 'Reggie', lastName: 'Report' },
        ],
      });

      expect(screen.getByRole('heading', { name: 'Direct Reports' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Dana Direct' })).toHaveAttribute(
        'href',
        '/compass/team-directory/10',
      );
      expect(screen.getByRole('link', { name: 'Reggie Report' })).toHaveAttribute(
        'href',
        '/compass/team-directory/11',
      );
    });

    it('renders a single column when there are 10 or fewer direct reports', () => {
      // Owner report: splitting a small team across two columns produces a lopsided (or, with a
      // row-wise grid, an interleaved) layout for the common case — a coach's own team should read
      // as one list, not two, until it outgrows a single column.
      const tenReports = Array.from({ length: 10 }, (_, index) => ({
        id: 10 + index,
        firstName: 'Report',
        lastName: `Number${index}`,
      }));
      renderDetail({ ...BASELINE_VIEW, directReports: tenReports });

      const section = screen.getByRole('heading', { name: 'Direct Reports' }).closest('section');
      expect(section).not.toBeNull();
      expect(within(section as HTMLElement).getAllByRole('list')).toHaveLength(1);
      tenReports.forEach((report) => {
        expect(screen.getByRole('link', { name: `Report ${report.lastName}` })).toBeInTheDocument();
      });
    });

    it('splits into two columns past 10, with exactly the first 10 in the left column', () => {
      // The owner's explicit split point: not a balanced half-and-half, but the first 10 on the
      // left (a column's own maximum) and everything past that on the right — so 12 reports read
      // as 10-and-2, not 6-and-6.
      const twelveReports = Array.from({ length: 12 }, (_, index) => ({
        id: 10 + index,
        firstName: 'Report',
        lastName: `Number${String(index).padStart(2, '0')}`,
      }));
      renderDetail({ ...BASELINE_VIEW, directReports: twelveReports });

      const section = screen.getByRole('heading', { name: 'Direct Reports' }).closest('section');
      expect(section).not.toBeNull();
      const columns = within(section as HTMLElement).getAllByRole('list');
      expect(columns).toHaveLength(2);
      expect(
        within(columns[0])
          .getAllByRole('link')
          .map((el) => el.textContent),
      ).toEqual([
        'Report Number00',
        'Report Number01',
        'Report Number02',
        'Report Number03',
        'Report Number04',
        'Report Number05',
        'Report Number06',
        'Report Number07',
        'Report Number08',
        'Report Number09',
      ]);
      expect(
        within(columns[1])
          .getAllByRole('link')
          .map((el) => el.textContent),
      ).toEqual(['Report Number10', 'Report Number11']);
    });

    it('paginates once there are more direct reports than fit on one page', async () => {
      const manyReports = Array.from({ length: 25 }, (_, index) => ({
        id: 100 + index,
        firstName: 'Report',
        lastName: `Number${String(index).padStart(2, '0')}`,
      }));
      renderDetail({ ...BASELINE_VIEW, directReports: manyReports });

      // 20 per page — the same size the rest of Compass's paginated lists default to.
      expect(screen.getByText(/showing 1 to 20 of 25/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Report Number00' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Report Number20' })).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Next' }));

      expect(screen.getByText(/showing 21 to 25 of 25/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Report Number20' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Report Number00' })).not.toBeInTheDocument();
    });

    it('offers no pager at all when everything fits on one page', () => {
      renderDetail({
        ...BASELINE_VIEW,
        directReports: [{ id: 10, firstName: 'Dana', lastName: 'Direct' }],
      });

      expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
    });

    it('marks a former direct report with the "Former" pill once revealed (issue #399)', async () => {
      // An elevated viewer can still reach former direct reports (issue #245), but must be able to
      // tell them apart from current ones without cross-referencing another screen.
      renderDetail({
        ...BASELINE_VIEW,
        directReports: [
          { id: 10, firstName: 'Dana', lastName: 'Direct', isActive: true },
          { id: 11, firstName: 'Reggie', lastName: 'Report', isActive: false },
        ],
      });

      await userEvent.click(screen.getByRole('switch', { name: /show former/i }));

      const activeReport = screen.getByRole('link', { name: 'Dana Direct' }).closest('li');
      const formerReport = screen.getByRole('link', { name: 'Reggie Report' }).closest('li');
      expect(activeReport).not.toBeNull();
      expect(formerReport).not.toBeNull();
      expect(within(activeReport as HTMLElement).queryByText('Former')).not.toBeInTheDocument();
      expect(within(formerReport as HTMLElement).getByText('Former')).toBeInTheDocument();
    });

    it('shows no "Former" pill when isActive is withheld (a baseline viewer)', () => {
      // A baseline viewer's direct reports never carry `isActive` (all-active already, BR-1) — the
      // pill must not appear from an absent field the way it would from an explicit `false`.
      renderDetail({
        ...BASELINE_VIEW,
        directReports: [{ id: 10, firstName: 'Dana', lastName: 'Direct' }],
      });

      expect(screen.queryByText('Former')).not.toBeInTheDocument();
    });
  });

  describe('"Show former" direct-reports toggle (issue #455)', () => {
    const MIXED_REPORTS = [
      { id: 10, firstName: 'Dana', lastName: 'Direct', isActive: true },
      { id: 11, firstName: 'Reggie', lastName: 'Report', isActive: false },
    ];

    it('shows only active direct reports by default, for a viewer who could see former ones', () => {
      // The default view is active-only (owner request) even though the payload carries both — the
      // same audience the "Former" pill has always been gated to (issue #399) is who gets the switch
      // that can reveal the rest.
      renderDetail({ ...BASELINE_VIEW, directReports: MIXED_REPORTS });

      expect(screen.getByRole('link', { name: 'Dana Direct' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Reggie Report' })).not.toBeInTheDocument();
    });

    it('reveals the former direct report once "Show former" is switched on', async () => {
      renderDetail({ ...BASELINE_VIEW, directReports: MIXED_REPORTS });

      const toggle = screen.getByRole('switch', { name: /show former/i });
      expect(toggle).toHaveAttribute('aria-checked', 'false');

      await userEvent.click(toggle);

      expect(toggle).toHaveAttribute('aria-checked', 'true');
      expect(screen.getByRole('link', { name: 'Dana Direct' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Reggie Report' })).toBeInTheDocument();
    });

    it('offers no toggle at all for a viewer who could never see former reports (a baseline viewer)', () => {
      // Every direct report a baseline viewer receives is already active (BR-1), so a switch would be
      // a no-op for them — restricted to the same audience that sees the "Former" tag (issue #399).
      renderDetail({
        ...BASELINE_VIEW,
        directReports: [{ id: 10, firstName: 'Dana', lastName: 'Direct' }],
      });

      expect(screen.queryByRole('switch', { name: /show former/i })).not.toBeInTheDocument();
    });

    it('still shows the section, with the toggle available, when every direct report is former', () => {
      // Hiding the whole section would strand a coach whose entire old team has left with no way to
      // reach them at all.
      renderDetail({
        ...BASELINE_VIEW,
        directReports: [{ id: 11, firstName: 'Reggie', lastName: 'Report', isActive: false }],
      });

      expect(screen.getByRole('heading', { name: 'Direct Reports' })).toBeInTheDocument();
      expect(screen.getByRole('switch', { name: /show former/i })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Reggie Report' })).not.toBeInTheDocument();
      expect(screen.getByText(/no active direct reports/i)).toBeInTheDocument();
    });

    it('places the toggle inline next to the "Direct Reports" heading', () => {
      renderDetail({ ...BASELINE_VIEW, directReports: MIXED_REPORTS });

      const heading = screen.getByRole('heading', { name: 'Direct Reports' });
      const toggle = screen.getByRole('switch', { name: /show former/i });
      // Same section, and the toggle immediately follows the heading in document order — the "Direct
      // Reports · Show former" layout the owner asked for, not one stranded elsewhere on the page.
      expect(heading.closest('section')).toBe(toggle.closest('section'));
      expect(
        heading.compareDocumentPosition(toggle) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
    });
  });

  it('renders a SOW that carries neither a note nor a rate increase', () => {
    // The elevated view where those two happen to be absent — distinct from being withheld.
    renderDetail({
      ...BASELINE_VIEW,
      assignmentHistory: [
        {
          assignmentId: 1,
          clientId: 1,
          clientName: 'Client One',
          startDate: '2022-01-01',
          endDate: null,
          sows: [{ sowType: 'InitialContract', startDate: '2022-01-01', endDate: '2023-01-01' }],
        },
      ],
    });

    expect(screen.getByText(/InitialContract/)).toBeInTheDocument();
    expect(screen.queryByText(/rate increase/i)).not.toBeInTheDocument();
  });

  it('renders an assignment whose sows array is present but empty', () => {
    renderDetail({
      ...BASELINE_VIEW,
      assignmentHistory: [
        {
          assignmentId: 1,
          clientId: 1,
          clientName: 'Client One',
          startDate: '2022-01-01',
          endDate: null,
          sows: [],
        },
      ],
    });

    expect(screen.getByRole('link', { name: 'Client One' })).toBeInTheDocument();
  });

  it('shows a loading state', () => {
    render(<EmployeeDetailPage detail={undefined} isPending isError={false} />);

    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows an error state', () => {
    render(<EmployeeDetailPage detail={undefined} isPending={false} isError />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('shows a not-found state when the record resolved to nothing', () => {
    // A baseline viewer requesting an inactive EDJEr gets a 404 by design — the page has to say so
    // rather than render an empty shell.
    render(<EmployeeDetailPage detail={undefined} isPending={false} isError={false} />);

    expect(screen.getByText(/not found/i)).toBeInTheDocument();
  });
});

/**
 * T078 (owner decision, 2026-08-25): the assignment history joins the card set below `md`.
 *
 * **Why it was a decision and not a defect fix.** This panel is the deliberate twin of the client
 * record's `CC-3` history (PR #223; spec 010 records them as parallel), and unlike that one it did
 * **not** strand — the audit measured no hidden width here. So the driver is consistency, not a
 * measured failure: one twin as cards and the other as a table is a visible inconsistency, and the
 * owner chose cards.
 *
 * jsdom has no `matchMedia`, so `useIsNarrowViewport` returns its desktop default and every test above
 * keeps asserting table semantics untouched. Only these opt in.
 */
describe('assignment history below md (T078)', () => {
  function stubNarrowViewport(isNarrow: boolean) {
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({
        matches: isNarrow,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    );
  }

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the history as a named list of cards, not a table', () => {
    stubNarrowViewport(true);

    renderDetail(ELEVATED_VIEW);

    // Same accessible name either way — a caller looking for "Assignment history" finds the same
    // dataset at both widths, which is what keeps it identifiable rather than viewport-dependent.
    expect(screen.getByRole('list', { name: 'Assignment history' })).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Assignment history' })).toBeNull();
  });

  it('carries every column label into the cards, in the same order', () => {
    stubNarrowViewport(true);

    renderDetail(ELEVATED_VIEW);

    const list = screen.getByRole('list', { name: 'Assignment history' });
    const labels = within(list)
      .getAllByRole('term')
      .map((term) => term.textContent);

    // Three columns, repeated once per assignment. A card that drops or reorders a label is a card
    // that mislabels a value, which is worse than a table nobody can scroll.
    expect(labels.slice(0, 3)).toEqual(['Client', 'Start', 'End']);
  });

  it('keeps the client link, the View assignment link, the note and the SOWs inside the card', () => {
    stubNarrowViewport(true);

    // `canViewAssignment` is granted explicitly, as the sibling test at "View assignment affordance"
    // does: the server OMITS the field rather than sending false, so a fixture that does not set it
    // is a viewer who was never granted the affordance. Asserting on it without granting it would
    // have tested the gate, not the card.
    renderDetail({
      ...ELEVATED_VIEW,
      assignmentHistory: ELEVATED_VIEW.assignmentHistory.map((a) => ({
        ...a,
        canViewAssignment: true,
      })),
    });

    const list = screen.getByRole('list', { name: 'Assignment history' });
    // The rich first cell is the reason this migration is not a one-liner: it holds a link, a second
    // gated link, prose and a nested list. All of it has to travel.
    expect(within(list).getByRole('link', { name: 'Client One' })).toBeInTheDocument();
    expect(within(list).getAllByRole('link', { name: 'View assignment' }).length).toBeGreaterThan(
      0,
    );
    expect(within(list).getByText(/SOW ·/)).toBeInTheDocument();
  });

  it('still says Current rather than a blank end date', () => {
    stubNarrowViewport(true);

    renderDetail(ELEVATED_VIEW);

    expect(
      within(screen.getByRole('list', { name: 'Assignment history' })).getByText('Current'),
    ).toBeInTheDocument();
  });

  it('exposes no scroll region below md — there is no off-screen axis left to strand on', () => {
    stubNarrowViewport(true);

    const { container } = renderDetail(ELEVATED_VIEW);

    // The ScrollableRegion this replaces existed to make the off-screen columns reachable. Cards have
    // no off-screen columns, so keeping it would leave a focusable wrapper around nothing.
    expect(container.querySelector('[data-scroll-edge-mask]')).toBeNull();
    expect(screen.queryByRole('group', { name: 'Assignment history' })).toBeNull();
  });

  it('keeps the table, and its scroll region, at or above md', () => {
    stubNarrowViewport(false);

    const { container } = renderDetail(ELEVATED_VIEW);

    expect(screen.getByRole('table', { name: 'Assignment history' })).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Assignment history' })).toBeNull();
    expect(container.querySelector('[data-scroll-edge-mask]')).not.toBeNull();
  });

  it('shows the empty state, not an empty card list, when there is no history', () => {
    stubNarrowViewport(true);

    renderDetail({ ...ELEVATED_VIEW, assignmentHistory: [] });

    expect(screen.getByText('No assignments on record.')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Assignment history' })).toBeNull();
  });
});
