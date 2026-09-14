import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

// The router is stubbed so these assert the panel, not routing.
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

const { ClientAssignmentHistory } =
  await import('../../../../src/features/clients/ClientAssignmentHistory');

const VIEW = '/api/compass/client-directory/4';
const ME = '/api/me';

/**
 * The client assignment history panel — AC-24, issue #224, mockup `CC-3`.
 *
 * The client-side twin of `EdjerAssignmentHistory` (issue #223), and shaped like it deliberately:
 * four data columns plus the SOW action, one status column carrying the **assignment's** status, and
 * an inactive EDJEr marked beside their name rather than in a column of their own (spec 010,
 * Clarifications Q2 and Departure 1).
 *
 * **The status column renders what the server derived** (FR-009, BR-11). Row 3 below deliberately
 * carries a status that contradicts what its dates "look like" — no end date, yet `Inactive` — so a
 * panel that re-derived from the dates would fail rather than quietly agreeing.
 */
const VIEW_BODY = {
  id: 4,
  clientName: 'Buckeye Mutual',
  status: 'Active',
  // Issue #518 — stated rather than absent, so every case below describes an ORDINARY client. The
  // internal variant lives in its own describe at the bottom of this file.
  isInternal: false,
  assignmentHistory: [
    {
      assignmentId: 1,
      employeeId: 11,
      employeeName: 'Maya Alvarez',
      startDate: '2024-04-01',
      endDate: null,
      employeeIsActive: true,
      canViewSow: true,
      status: 'Active',
    },
    {
      assignmentId: 2,
      employeeId: 12,
      employeeName: 'Chris Doyle',
      startDate: '2022-05-02',
      endDate: '2023-12-20',
      employeeIsActive: false,
      canViewSow: true,
      status: 'Inactive',
    },
    {
      // Contradicts its own dates on purpose — see the block comment above.
      assignmentId: 3,
      employeeId: 13,
      employeeName: 'Ada Lovelace',
      startDate: '2021-01-01',
      endDate: null,
      employeeIsActive: true,
      canViewSow: false,
      status: 'Inactive',
    },
  ],
};

/**
 * The privilege strings are SPACE-separated and carry a `'Compass '` prefix — `getCompassPrivileges`
 * filters on exactly that, so a run-together `'CompassSuperAdmin'` is discarded before any role check
 * sees it and every affordance silently disappears. Not the same vocabulary as the Google GROUP names,
 * which are space-free (`Compass-SuperAdmin`); groups map to these role strings at sign-in.
 */
const SUPER_ADMIN_USER = {
  edjeId: 'e-1',
  email: 'super.admin@leadingedje.com',
  displayName: 'Super Admin',
  privileges: ['Compass Super Admin'],
  impersonator: null,
};

/** Compass Admin: elevated for VISIBILITY, read-only for capability (AC-44, BR-1). */
const READ_ONLY_USER = { ...SUPER_ADMIN_USER, privileges: ['Compass Admin'] };

function renderPanel(stub: FetchByUrlStub) {
  vi.stubGlobal('fetch', stub.mock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <ClientAssignmentHistory clientId={4} />
    </QueryClientProvider>,
  );
}

function stub(overrides: Record<string, { status: number; body?: unknown }> = {}) {
  return stubFetchByUrl({
    [`GET ${VIEW}`]: { status: 200, body: VIEW_BODY },
    [`GET ${ME}`]: { status: 200, body: SUPER_ADMIN_USER },
    ...overrides,
  });
}

async function rows() {
  const table = await screen.findByRole('table', { name: /assignment history/i });
  return within(table).getAllByRole('row').slice(1); // drop the header row
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('ClientAssignmentHistory', () => {
  it('is a labelled region, so the card is reachable and announced', async () => {
    renderPanel(stub());

    expect(
      await screen.findByRole('region', { name: /edjer assignment history/i }),
    ).toBeInTheDocument();
  });

  it('lists every EDJEr ever assigned, ended ones included — it is a history, not a roster', async () => {
    renderPanel(stub());

    expect(await rows()).toHaveLength(3);
    expect(screen.getByText('Maya Alvarez')).toBeInTheDocument();
    expect(screen.getByText('Chris Doyle')).toBeInTheDocument();
  });

  it('renders CC-3 column headers in the mockup order, with one status column', async () => {
    renderPanel(stub());

    const table = await screen.findByRole('table', { name: /assignment history/i });
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((cell) => cell.textContent?.trim());

    expect(headers).toEqual(['EDJEr', 'Status', 'Started', 'Ended', 'SOW']);
  });

  it('shows each assignment status as a word the server sent, not a colour', async () => {
    renderPanel(stub());
    const [first] = await rows();

    // A word, because a pill distinguished only by colour conveys nothing in greyscale and nothing at
    // all to a screen reader (AC-NFR-5).
    expect(within(first).getByText('Active')).toBeInTheDocument();
  });

  it('renders the status the server sent even when the dates suggest otherwise', async () => {
    renderPanel(stub());
    const [, , third] = await rows();

    // Row 3 has NO end date, which every naive local derivation reads as current. The server says
    // Inactive. If this row renders "Active", something in the browser is deriving status — which
    // ClientStatusSingleDerivationTests forbids and AC-42 names as the comparison implementations
    // get wrong.
    expect(within(third).getByText('Inactive')).toBeInTheDocument();
    expect(within(third).queryByText('Active')).not.toBeInTheDocument();
  });

  it('marks a departed EDJEr beside their name rather than in a column of its own', async () => {
    renderPanel(stub());
    const [, second] = await rows();

    // Departure 1 / Q2: CC-3's "EDJEr Status" column becomes a Former pill on the name cell, which is
    // what ClientViewPage already ships. AC-15's disclosure is kept, not dropped.
    expect(within(second).getByText('Former')).toBeInTheDocument();
  });

  it('marks nothing when the server withheld the EDJEr active flag', async () => {
    // Absent means "not entitled to know", and every row such a viewer receives is active. Rendering
    // a Former or Unknown pill from an absent value would state something the server did not say.
    const body = {
      ...VIEW_BODY,
      assignmentHistory: [{ ...VIEW_BODY.assignmentHistory[1], employeeIsActive: undefined }],
    };
    renderPanel(stub({ [`GET ${VIEW}`]: { status: 200, body } }));

    const [only] = await rows();
    expect(within(only).queryByText('Former')).not.toBeInTheDocument();
  });

  it('formats dates the way every other Compass screen does', async () => {
    renderPanel(stub());
    const [, second] = await rows();

    expect(within(second).getByText('05/02/2022')).toBeInTheDocument();
    expect(within(second).getByText('12/20/2023')).toBeInTheDocument();
  });

  it('marks an open-ended assignment without restating the status beside it', async () => {
    renderPanel(stub());
    const [first] = await rows();

    // An em dash, not "Current": the Status column already published that judgement, and row 3 proves
    // the two can disagree — a null end date the server calls Inactive would read "Inactive … Current".
    expect(within(first).getByText('No end date')).toBeInTheDocument();
    expect(within(first).queryByText('Current')).not.toBeInTheDocument();
  });

  it('links each row to the EDJEr, so the history is a way into their record', async () => {
    renderPanel(stub());
    const [first] = await rows();

    expect(within(first).getByRole('link', { name: 'Maya Alvarez' })).toHaveAttribute(
      'href',
      '/team-directory/11',
    );
  });

  it('gives both of its links the one shared table-link style', async () => {
    // Both diverged from the baseline and from each other: `brand-gray` + `font-medium` on the name,
    // no green decoration at all on View SOW (owner request 2026-08-21).
    renderPanel(stub());
    const [first] = await rows();

    expect(within(first).getByRole('link', { name: 'Maya Alvarez' }).className).toBe(
      tableLinkClass,
    );
    expect(within(first).getByRole('link', { name: /view sow/i }).className).toBe(tableLinkClass);
  });

  it('says the client has no assignments rather than rendering an empty table', async () => {
    renderPanel(
      stub({ [`GET ${VIEW}`]: { status: 200, body: { ...VIEW_BODY, assignmentHistory: [] } } }),
    );

    // A brand-new client is exactly this case, and it is fully assignable — so the message must not
    // read as a problem.
    expect(await screen.findByText(/no edjers have been assigned/i)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('reports a refusal as a refusal, never as an empty history', async () => {
    // `null` is the hook's answer for a 404 OR a record this viewer may not see. Either way it is a
    // failure to load, and calling it "no assignments" would state something false about the client.
    renderPanel(stub({ [`GET ${VIEW}`]: { status: 404 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded/i);
  });

  // ----------------------------------------------------------------- US4, the two affordances

  it('offers the assign-a-new-EDJEr entry point from the client record (AC-24, AC-27)', async () => {
    renderPanel(stub());

    expect(await screen.findByRole('link', { name: /assign edjer/i })).toHaveAttribute(
      'href',
      '/client-directory/4/assignments/new',
    );
  });

  it('withholds the assign entry point from a viewer without write authority', async () => {
    // Compass Admin is elevated for VISIBILITY and read-only for capability (AC-44, BR-1) — the pair
    // that makes "elevated means may edit" a wrong inference. The server refuses the write regardless;
    // this only stops the control from lying about it.
    renderPanel(stub({ [`GET ${ME}`]: { status: 200, body: READ_ONLY_USER } }));

    expect(await screen.findByRole('table', { name: /assignment history/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /assign edjer/i })).not.toBeInTheDocument();
  });

  it('offers View SOW only where the server granted it (AC-16)', async () => {
    renderPanel(stub());
    const [first, , third] = await rows();

    expect(within(first).getByRole('link', { name: /view sow/i })).toHaveAttribute(
      'href',
      '/client-directory/4/assignments/1',
    );

    // Row 3's canViewSow is false. An entitlement the server withheld cannot be rendered from a
    // client-side guess.
    expect(within(third).queryByRole('link', { name: /view sow/i })).not.toBeInTheDocument();
  });

  describe('an internal client has no contracts at all (issue #518)', () => {
    const INTERNAL_VIEW = { ...VIEW_BODY, clientName: 'EDJE', isInternal: true };

    function internalStub() {
      return stub({ [`GET ${VIEW}`]: { status: 200, body: INTERNAL_VIEW } });
    }

    it('drops the SOW column entirely', async () => {
      // The whole column, not merely the cells: the section it links into does not exist for an
      // internal client at ANY tier, so there is nothing for the header to label. Same call
      // `ClientViewPage` makes on the same data.
      renderPanel(internalStub());

      const table = await screen.findByRole('table', { name: /assignment history/i });
      const headers = within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent?.trim());

      expect(headers).toEqual(['EDJEr', 'Status', 'Started', 'Ended']);
    });

    it('offers no View SOW link on any row, even where the server granted the entitlement', async () => {
      // Row 1's `canViewSow` is true. The server's entitlement is about PERMISSION; this is about
      // whether the destination has anything on it.
      renderPanel(internalStub());
      const all = await rows();

      for (const row of all) {
        expect(within(row).queryByRole('link', { name: /view sow/i })).not.toBeInTheDocument();
      }
    });

    it('keeps every cell paired with its own header', async () => {
      // The cell array pairs with `COLUMNS` BY INDEX, so a length mismatch mislabels values silently
      // rather than failing — a wrong date under "Ended" reads as data, not as a bug.
      renderPanel(internalStub());
      const table = await screen.findByRole('table', { name: /assignment history/i });
      const [first] = await rows();

      expect(within(first).getAllByRole('cell')).toHaveLength(
        within(table).getAllByRole('columnheader').length,
      );
    });
  });
});
