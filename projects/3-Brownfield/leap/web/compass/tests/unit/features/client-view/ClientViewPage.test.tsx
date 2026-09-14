import { render, screen, within } from '@testing-library/react';
import { buttonClassName, tableLinkClass } from '../../../../src/components/ui-classes';
import { ClientViewPage } from '../../../../src/features/client-view/ClientViewPage';
import type { ClientView } from '../../../../src/features/client-view/types';

/** What a BASELINE viewer receives: name, status, history — and no details panel, no SOW affordance. */
const BASELINE_VIEW: ClientView = {
  id: 1,
  clientName: 'Currently Engaged',
  status: 'Active',
  isInternal: false,
  assignmentHistory: [
    {
      assignmentId: 1,
      employeeId: 1,
      employeeName: 'Ada Active',
      startDate: '2022-01-01',
      endDate: null,
      status: 'Active',
    },
  ],
};

/** What an ELEVATED viewer receives: the same, plus inactive EDJErs and the SOW affordance. */
const ELEVATED_VIEW: ClientView = {
  ...BASELINE_VIEW,
  assignmentHistory: [
    {
      assignmentId: 1,
      employeeId: 1,
      employeeName: 'Ada Active',
      startDate: '2022-01-01',
      endDate: null,
      status: 'Active',
      employeeIsActive: true,
      canViewSow: true,
    },
    {
      assignmentId: 2,
      employeeId: 2,
      employeeName: 'Ivor Inactive',
      startDate: '2021-01-01',
      endDate: '2022-06-30',
      status: 'Inactive',
      employeeIsActive: false,
      canViewSow: true,
    },
  ],
};

/** What a SUPER ADMIN receives: everything, including the configuration panel. */
const SUPER_ADMIN_VIEW: ClientView = {
  ...ELEVATED_VIEW,
  clientDetails: {
    msaSignedDate: '2023-01-01',
    ndaSignedDate: '2023-02-01',
    isInternal: false,
    invoiceFrequency: 'Monthly',
    billableTimeCategories: ['Development'],
  },
};

function renderView(view: ClientView, canManageAssignments = false) {
  return render(
    <ClientViewPage
      view={view}
      isPending={false}
      isError={false}
      canManageAssignments={canManageAssignments}
    />,
  );
}

describe('ClientViewPage', () => {
  it('shows the client name for a viewer with no details panel (AC-13)', () => {
    // The criterion exists because AC-14 hides the panel a name would naturally live in.
    renderView(BASELINE_VIEW);

    expect(
      screen.getByRole('heading', { name: 'Currently Engaged', level: 1 }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/client details/i)).not.toBeInTheDocument();
  });

  it('renders the name OUTSIDE the client details panel, so it survives that panel being hidden', () => {
    renderView(SUPER_ADMIN_VIEW);

    const heading = screen.getByRole('heading', { name: 'Currently Engaged', level: 1 });
    const detailsHeading = screen.getByRole('heading', { name: /client details/i });

    // The name must not be nested inside the details section — the whole point of AC-13.
    expect(detailsHeading.closest('section')?.contains(heading)).toBe(false);
  });

  it('shows the derived status', () => {
    renderView(BASELINE_VIEW);

    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('omits the client details panel when the server withheld it (AC-14)', () => {
    renderView(ELEVATED_VIEW);

    expect(screen.queryByText(/msa signed/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/invoice frequency/i)).not.toBeInTheDocument();
  });

  it('shows the client details panel when the server sent it (AC-14)', () => {
    renderView(SUPER_ADMIN_VIEW);

    expect(screen.getByText(/msa signed/i)).toBeInTheDocument();
    expect(screen.getByText('Monthly')).toBeInTheDocument();
    expect(screen.getByText('Development')).toBeInTheDocument();
  });

  it('formats the MSA/NDA signed dates as mm/dd/yyyy rather than the raw yyyy-mm-dd wire shape (#234)', () => {
    renderView(SUPER_ADMIN_VIEW);

    expect(screen.getByText('01/01/2023')).toBeInTheDocument();
    expect(screen.getByText('02/01/2023')).toBeInTheDocument();
  });

  it('shows start date, end date and EDJEr status per history row (AC-15)', () => {
    renderView(ELEVATED_VIEW);

    const row = screen.getByRole('row', { name: /Ivor Inactive/ });
    expect(within(row).getByText('01/01/2021')).toBeInTheDocument();
    expect(within(row).getByText('06/30/2022')).toBeInTheDocument();
    expect(within(row).getByText('Former')).toBeInTheDocument();
  });

  it('leaves the end date blank for an open-ended assignment, rather than writing "Current"', () => {
    // The issue is explicit: no end date means the cell is blank, not the word "Current" — the
    // section heading already says that.
    renderView(BASELINE_VIEW);

    const row = screen.getByRole('row', { name: /Ada Active/ });
    expect(row).not.toHaveTextContent('Current');
    expect(within(row).getByText('01/01/2022')).toBeInTheDocument();
  });

  it('labels the detail fields as the design source labels them (FR-014)', () => {
    // `Date MSA Signed` / `Date NDA Signed`, from mockup screen `#s-client`. They shipped as
    // "MSA signed" / "NDA signed".
    renderView(SUPER_ADMIN_VIEW);

    expect(screen.getByText('Date MSA Signed')).toBeInTheDocument();
    expect(screen.getByText('Date NDA Signed')).toBeInTheDocument();
  });

  it('labels the history columns as the design source labels them (FR-014)', () => {
    // `Start Date` / `End Date`, shipped as the abbreviated `Start` / `End`. ELEVATED_VIEW splits
    // across both the current and former tables, so both must carry the same labels.
    renderView(ELEVATED_VIEW);

    const tables = screen.getAllByRole('table', { name: /assignment history/i });
    expect(tables).toHaveLength(2);
    tables.forEach((table) => {
      expect(within(table).getByRole('columnheader', { name: 'Start Date' })).toBeInTheDocument();
      expect(within(table).getByRole('columnheader', { name: 'End Date' })).toBeInTheDocument();
    });
  });

  it('gives the assignment dates tabular figures, so the SOW column cannot reflow', () => {
    // Same reasoning as the Team Directory's hire date: an auto-layout table derives a column's width
    // from its content, the seeded assignment dates are relative to `today`, and proportional figures
    // therefore shift every column to the right of these two as the calendar moves. This screen had
    // not failed yet only because it shows seven rows of dates rather than twenty, so its drift stayed
    // under the 1% tolerance of the screenshot gate that used to exist — the same latent bug, not a
    // healthy screen. Issue #574 deleted that gate on 2026-09-08, so this assertion is now the ONLY
    // thing holding `tabular-nums` on these two columns.
    renderView(ELEVATED_VIEW);

    const table = screen.getByRole('table', { name: /current.*assignment history/i });
    const startCell = within(table).getByText('01/01/2022');

    expect(startCell.className).toMatch(/(?:^|\s)tabular-nums(?:\s|$)/);
  });

  it('sizes its heading like every other Compass page', () => {
    // Two h1 sizes shipped: `text-xl` from `PageHeader` and `text-2xl` hand-rolled here and on the
    // client directory (feature 008 FR-020).
    renderView(BASELINE_VIEW);

    const heading = screen.getByRole('heading', { level: 1, name: 'Currently Engaged' });
    expect(heading.className).toContain('text-xl');
    expect(heading.className).not.toContain('text-2xl');
  });

  it('renders a Former badge through the shared StatusPill, not a fourth hand-rolled copy', () => {
    // The same taupe chip had been hand-rolled in three places. StatusPill also states the status as
    // a WORD, which is the AC-NFR-5 requirement the hand-rolled span happened to satisfy by accident.
    renderView(ELEVATED_VIEW);

    const row = screen.getByRole('row', { name: /Ivor Inactive/ });
    expect(within(row).getByText('Former')).toBeInTheDocument();
  });

  it('links each EDJEr back to their detail', () => {
    renderView(BASELINE_VIEW);

    expect(screen.getByRole('link', { name: 'Ada Active' })).toHaveAttribute(
      'href',
      '/compass/team-directory/1',
    );
  });

  it('gives both of its links the one shared table-link style', () => {
    // Two variants of the baseline in one table: no green decoration, then `brand-gray` (owner
    // request 2026-08-21).
    renderView(ELEVATED_VIEW);

    expect(screen.getByRole('link', { name: 'Ada Active' }).className).toBe(tableLinkClass);
    expect(screen.getAllByRole('link', { name: /view sow/i })[0].className).toBe(tableLinkClass);
  });

  it('withholds the View-SOW affordance when the server did not grant it (AC-16)', () => {
    renderView(BASELINE_VIEW);

    expect(screen.queryByRole('link', { name: /view sow/i })).not.toBeInTheDocument();
  });

  it('offers the View-SOW affordance where the server granted it (AC-16)', () => {
    renderView(ELEVATED_VIEW);

    expect(screen.getAllByRole('link', { name: /view sow/i })).toHaveLength(2);
  });

  it('the View-SOW affordance reaches the assignment detail screen, not the EDJEr overview (AC-1, AC-2)', () => {
    // Deviation 4 (spec 006) flagged this as a placeholder pointing at the wrong screen; this is that
    // placeholder closed — it must reach the client-nested assignment route, keyed by assignmentId.
    renderView(ELEVATED_VIEW);

    expect(screen.getAllByRole('link', { name: /view sow/i })[0]).toHaveAttribute(
      'href',
      '/compass/client-directory/1/assignments/1',
    );
  });

  it('hides the SOW column entirely for an internal client, for every tier (issue #243)', () => {
    // An internal (EDJE-to-EDJE) client has no contracts, so the column has nothing to link to for
    // ANY viewer — not merely no affordance for baseline. `isInternal` is served at the top level
    // (unlike the rest of Client Details) specifically so a baseline or elevated viewer, who never
    // receives `clientDetails`, can still make this call.
    renderView({ ...ELEVATED_VIEW, isInternal: true });

    expect(screen.queryByRole('columnheader', { name: /^sow$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /view sow/i })).not.toBeInTheDocument();
  });

  it('shows the SOW column for a non-internal client where the server granted the affordance', () => {
    renderView({ ...ELEVATED_VIEW, isInternal: false });

    expect(screen.getAllByRole('columnheader', { name: /^sow$/i }).length).toBeGreaterThan(0);
  });

  describe('"New assignment" action (AC-1, AC-2, feature 006)', () => {
    it('is hidden by default (AC-44 — a usability affordance, not the authorization boundary)', () => {
      renderView(BASELINE_VIEW);

      expect(screen.queryByRole('link', { name: /new assignment/i })).not.toBeInTheDocument();
    });

    it("appears when the viewer can manage assignments, linking to this client's new-assignment route", () => {
      renderView(BASELINE_VIEW, true);

      expect(screen.getByRole('link', { name: 'New Assignment' })).toHaveAttribute(
        'href',
        '/compass/client-directory/1/assignments/new',
      );
    });

    it('wears the primary button appearance, matching the same action on the EDJEr record', () => {
      // Owner report 2026-08-18. The two entry points are the same action from two records; they
      // shipped sharing a pale-green class run and now share `buttonClassName` instead.
      //
      // `size: 'small'` per the rule on the helper (#336) — a panel action, not a page-header one.
      renderView(BASELINE_VIEW, true);

      expect(screen.getByRole('link', { name: 'New Assignment' }).className).toContain(
        buttonClassName({ variant: 'primary', size: 'small' }),
      );
    });
  });

  it('shows an empty history rather than an error for a never-assigned client', () => {
    renderView({ ...BASELINE_VIEW, assignmentHistory: [], status: 'Inactive' });

    expect(screen.getByText(/no edjers have been assigned/i)).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Currently Engaged', level: 1 }),
    ).toBeInTheDocument();
    // Neither sub-section heading has anything to introduce.
    expect(
      screen.queryByRole('heading', { name: 'Current Client Assignments' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Former Client Assignments' }),
    ).not.toBeInTheDocument();
  });

  describe('current vs. former sections (issue #658)', () => {
    /** A client with one current and two former assignments, seeded out of end-date order. */
    const SPLIT_VIEW: ClientView = {
      ...BASELINE_VIEW,
      assignmentHistory: [
        {
          assignmentId: 1,
          employeeId: 1,
          employeeName: 'Ada Active',
          startDate: '2024-01-01',
          endDate: null,
          status: 'Active',
        },
        {
          assignmentId: 2,
          employeeId: 2,
          employeeName: 'Fiona Former',
          startDate: '2020-01-01',
          endDate: '2021-06-30',
          status: 'Inactive',
        },
        {
          assignmentId: 3,
          employeeId: 3,
          employeeName: 'Gary Gone',
          startDate: '2021-07-01',
          endDate: '2023-12-31',
          status: 'Inactive',
        },
      ],
    };

    it('keeps "EDJEr Assignment History" as the overall title, with the two new headings underneath', () => {
      renderView(SPLIT_VIEW);

      expect(
        screen.getByRole('heading', { name: 'EDJEr Assignment History', level: 2 }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole('heading', { name: 'Current Client Assignments', level: 3 }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole('heading', { name: 'Former Client Assignments', level: 3 }),
      ).toBeInTheDocument();
    });

    it('puts an assignment with no end date, or an end date in the future, under Current', () => {
      renderView(SPLIT_VIEW);

      const currentTable = screen.getByRole('table', { name: /current.*assignment history/i });
      expect(within(currentTable).getByText('Ada Active')).toBeInTheDocument();
      expect(within(currentTable).queryByText('Fiona Former')).not.toBeInTheDocument();
    });

    it('puts an assignment with an end date in the past under Former', () => {
      renderView(SPLIT_VIEW);

      const formerTable = screen.getByRole('table', { name: /former.*assignment history/i });
      expect(within(formerTable).getByText('Fiona Former')).toBeInTheDocument();
      expect(within(formerTable).getByText('Gary Gone')).toBeInTheDocument();
      expect(within(formerTable).queryByText('Ada Active')).not.toBeInTheDocument();
    });

    it('sorts Former by end date descending, so the most recently-ended assignment shows first', () => {
      renderView(SPLIT_VIEW);

      const formerTable = screen.getByRole('table', { name: /former.*assignment history/i });
      const names = within(formerTable)
        .getAllByRole('row')
        .slice(1) // drop the header row
        .map((row) => within(row).getByRole('link').textContent);

      // Gary Gone ended 2023-12-31, after Fiona Former's 2021-06-30.
      expect(names).toEqual(['Gary Gone', 'Fiona Former']);
    });

    it('hides the Former heading and table entirely when every assignment is current', () => {
      renderView({ ...SPLIT_VIEW, assignmentHistory: [SPLIT_VIEW.assignmentHistory[0]] });

      expect(
        screen.queryByRole('heading', { name: 'Former Client Assignments' }),
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('table', { name: /former.*assignment history/i }),
      ).not.toBeInTheDocument();
    });

    it('hides the Current heading and table entirely when every assignment is former', () => {
      renderView({
        ...SPLIT_VIEW,
        assignmentHistory: SPLIT_VIEW.assignmentHistory.filter((row) => row.status === 'Inactive'),
      });

      expect(
        screen.queryByRole('heading', { name: 'Current Client Assignments' }),
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('table', { name: /current.*assignment history/i }),
      ).not.toBeInTheDocument();
    });

    it('buckets by the server-derived status, not by recomputing from the dates (AC-42)', () => {
      // A row can carry a future end date and still be `Active` — the business-date comparison is
      // the server's job, never the browser's.
      renderView({
        ...BASELINE_VIEW,
        assignmentHistory: [
          {
            assignmentId: 9,
            employeeId: 9,
            employeeName: 'Priya Planned',
            startDate: '2026-01-01',
            endDate: '2099-01-01',
            status: 'Active',
          },
        ],
      });

      const currentTable = screen.getByRole('table', { name: /current.*assignment history/i });
      expect(within(currentTable).getByText('Priya Planned')).toBeInTheDocument();
      expect(
        screen.queryByRole('table', { name: /former.*assignment history/i }),
      ).not.toBeInTheDocument();
    });
  });

  it('renders em dashes for absent detail values', () => {
    renderView({
      ...SUPER_ADMIN_VIEW,
      clientDetails: {
        msaSignedDate: null,
        ndaSignedDate: null,
        isInternal: false,
        invoiceFrequency: null,
        billableTimeCategories: [],
      },
    });

    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(3);
  });

  it('hides MSA Signed, NDA Signed and Invoice Frequency for an internal client (issue #243)', () => {
    // Leading EDJE is internal to itself: there is no MSA/NDA to sign and nothing to invoice, so
    // those fields have no meaning for an internal client and must not display at all — not even
    // as an em dash.
    renderView({
      ...SUPER_ADMIN_VIEW,
      clientDetails: {
        msaSignedDate: '2023-01-01',
        ndaSignedDate: '2023-02-01',
        isInternal: true,
        invoiceFrequency: 'Monthly',
        billableTimeCategories: ['Internal — Non-Billable'],
      },
    });

    expect(screen.queryByText(/msa signed/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/nda signed/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/invoice frequency/i)).not.toBeInTheDocument();

    // The internal flag itself and everything else on the panel still shows.
    expect(screen.getByText('Internal')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();
    expect(screen.getByText('Internal — Non-Billable')).toBeInTheDocument();
  });

  it('shows a loading state', () => {
    render(<ClientViewPage view={undefined} isPending isError={false} />);

    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows an error state', () => {
    render(<ClientViewPage view={undefined} isPending={false} isError />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('shows a not-found state', () => {
    render(<ClientViewPage view={null} isPending={false} isError={false} />);

    expect(screen.getByText(/not found/i)).toBeInTheDocument();
  });
});
