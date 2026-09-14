import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { buttonClassName, tableLinkClass } from '../../../../src/components/ui-classes';
import { checkAccessibility } from '../../../../src/lib/accessibility-check';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

// The router is stubbed so these assert the panel, not routing.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    params,
    search,
    children,
    ...rest
  }: {
    to: string;
    params?: Record<string, string>;
    search?: Record<string, string>;
    children: React.ReactNode;
  } & Record<string, unknown>) => {
    const path = Object.entries(params ?? {}).reduce(
      (built, [key, value]) => built.replace(`$${key}`, value),
      to,
    );
    // Serialised, not spread: a mock that dropped `search` would let a missing prop pass.
    const query = new URLSearchParams(search ?? {}).toString();
    return (
      <a href={query === '' ? path : `${path}?${query}`} {...rest}>
        {children}
      </a>
    );
  },
}));

const { EdjerAssignmentHistory } =
  await import('../../../../src/features/edjers/EdjerAssignmentHistory');

const DETAIL = '/api/compass/team-directory/7';

/**
 * The EDJEr assignment history panel — AC-20, issue #223, mockup `EC-3`.
 *
 * Both affordances navigate, carrying `?from=admin` so the destination breadcrumbs back to this form.
 *
 * **The fixture rows below deliberately carry NO `canViewAssignment`**, matching what the server sends a
 * viewer it has not granted the assignment to: the field is OMITTED, never sent false. That is why the
 * granted case has its own fixture rather than being asserted against these rows — and it is why the
 * absence assertion here would otherwise pass for the wrong reason, which is exactly what happened
 * while the link did not exist at all.
 *
 * **The status column renders what the server derived** (FR-029). The fixtures below deliberately set a
 * status that contradicts what the dates "look like", so a panel that re-derived locally would fail.
 */
const DETAIL_BODY = {
  id: 7,
  firstName: 'Maya',
  lastName: 'Alvarez',
  hireDate: '2016-03-14',
  email: 'maya.alvarez@example.test',
  employeeType: 'Full Time',
  coach: null,
  state: 'OH',
  assignmentHistory: [
    {
      assignmentId: 1,
      clientId: 11,
      clientName: 'Contoso',
      startDate: '2022-01-01',
      endDate: null,
      status: 'Active',
      // Issue #518 — stated rather than absent. Both fixture rows are ORDINARY clients; the internal
      // case has its own describe below, because an EDJEr's history genuinely mixes the two.
      isInternal: false,
    },
    {
      assignmentId: 2,
      clientId: 12,
      clientName: 'Fabrikam',
      startDate: '2021-01-01',
      endDate: '2023-06-30',
      status: 'Inactive',
      isInternal: false,
    },
  ],
};

function renderPanel(stub: FetchByUrlStub) {
  vi.stubGlobal('fetch', stub.mock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <EdjerAssignmentHistory edjerId={7} />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('EdjerAssignmentHistory', () => {
  it('is a labelled region named Client Assignment History', async () => {
    // Exact, not /assignment history/i: the owner named this panel for what its rows ARE — an EDJEr's
    // engagements with clients (2026-08-18) — and a loose regex matches the old title just as well.
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    expect(
      await screen.findByRole('region', { name: 'Client Assignment History' }),
    ).toBeInTheDocument();
  });

  it('closes each row with a SOW column', async () => {
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    const table = await screen.findByRole('table', { name: /client assignment history/i });
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((header) => header.textContent?.trim());

    expect(headers).toEqual(['Client', 'Status', 'Started', 'Ended', 'SOW']);
  });

  it('lists every assignment, ended ones included — it is a history, not a snapshot', async () => {
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    expect(await screen.findByText('Contoso')).toBeInTheDocument();
    expect(screen.getByText('Fabrikam')).toBeInTheDocument();
  });

  it('shows each assignment status as a word the server sent, not a colour', async () => {
    // AC-NFR-5: a cell meaning something only by hue conveys nothing in greyscale or to a screen
    // reader. AC-20 asks for the status; this is where it lands.
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    const current = await screen.findByRole('row', { name: /contoso/i });
    expect(within(current).getByText('Active')).toBeInTheDocument();

    const ended = screen.getByRole('row', { name: /fabrikam/i });
    expect(within(ended).getByText('Inactive')).toBeInTheDocument();
  });

  it('renders the status the server sent even when the dates suggest otherwise', async () => {
    // The load-bearing assertion (FR-029, BR-11). An open-ended assignment "looks" current, so a
    // panel deciding for itself would say Active. The server said Inactive — because only it knows
    // the business date and the inclusive boundary — and Inactive is what must render.
    renderPanel(
      stubFetchByUrl({
        [`GET ${DETAIL}`]: {
          status: 200,
          body: {
            ...DETAIL_BODY,
            assignmentHistory: [
              {
                assignmentId: 3,
                clientId: 13,
                clientName: 'Northwind',
                startDate: '2022-01-01',
                endDate: null,
                status: 'Inactive',
              },
            ],
          },
        },
      }),
    );

    const row = await screen.findByRole('row', { name: /northwind/i });
    expect(within(row).getByText('Inactive')).toBeInTheDocument();
    expect(within(row).queryByText('Active')).not.toBeInTheDocument();
  });

  it('formats dates the way every other Compass screen does', async () => {
    // MM/DD/YYYY via the shared lib/date formatDate, NOT the raw ISO the API sends. This assertion
    // was written the wrong way round first — it expected "2022-01-01" and so locked IN the defect:
    // the same EmployeeAssignment.startDate renders as "01/01/2022" on EmployeeDetailPage, and one
    // screen showing a different format for identical data is the thing #234's shared helper exists
    // to prevent. Asserting the ISO form is ABSENT is what makes this a regression test rather than
    // a restatement.
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    const current = await screen.findByRole('row', { name: /contoso/i });
    expect(within(current).getByText('01/01/2022')).toBeInTheDocument();
    expect(within(current).queryByText('2022-01-01')).not.toBeInTheDocument();

    const ended = screen.getByRole('row', { name: /fabrikam/i });
    expect(within(ended).getByText('06/30/2023')).toBeInTheDocument();
    expect(within(ended).queryByText('2023-06-30')).not.toBeInTheDocument();
  });

  it('marks an open-ended assignment without restating the status beside it', async () => {
    // An empty cell reads as missing data, so the cell says something — but NOT "Current", which is
    // what EmployeeDetailPage uses. That screen has no status column, so there the word carries the
    // meaning; here it would put a derived judgement in a date column next to the Status column that
    // already published one, and the two can visibly disagree (see the next test's fixture).
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    const current = await screen.findByRole('row', { name: /contoso/i });
    expect(within(current).getByText('No end date')).toBeInTheDocument();
  });

  it('links each row to the client, with the client named in the accessible name', async () => {
    // AC-8's rule, reused: a client in an assignment history links to that client's view.
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    const link = await screen.findByRole('link', { name: /contoso/i });
    expect(link).toHaveAttribute('href', '/client-directory/11');
  });

  it('gives the client link the one shared table-link style', async () => {
    // It was `brand-gray` + `font-medium`, and View SOWs beside it was the only `font-semibold` link
    // in Compass (owner request 2026-08-21).
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

    const link = await screen.findByRole('link', { name: /contoso/i });
    expect(link.className).toBe(tableLinkClass);
  });

  it('says the EDJEr has no assignments rather than rendering an empty table', async () => {
    // Every brand-new EDJEr is in this state, and it must not read as an error.
    renderPanel(
      stubFetchByUrl({
        [`GET ${DETAIL}`]: { status: 200, body: { ...DETAIL_BODY, assignmentHistory: [] } },
      }),
    );

    expect(await screen.findByText(/no assignments yet/i)).toBeInTheDocument();
  });

  it('reports a refusal as a refusal, never as an empty history', async () => {
    renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 403 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded|permission/i);
    expect(screen.queryByText(/no assignments yet/i)).not.toBeInTheDocument();
  });

  describe('the assign affordance (#448, mockup EC-3)', () => {
    /**
     * #448 reverses this file's earlier assertion that no such control existed — its premise (no
     * endpoint) stopped being true when `POST /api/compass/assignments` shipped. Label is the
     * mockup's own, mirroring the client panel's `+ Assign EDJEr`.
     */
    const ACTIVE = { ...DETAIL_BODY, isActive: true };

    it('offers the action in the card heading, as a link to the new-assignment screen', async () => {
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: ACTIVE } }));

      const link = await screen.findByRole('link', { name: '+ Assign to Client' });

      expect(link).toHaveAttribute('href', '/team-directory/7/assignments/new?from=admin');
    });

    it('wears the shared primary-small button appearance rather than a copied class run', async () => {
      // Asserted through the same helper the client twin uses, so the two cannot drift apart.
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: ACTIVE } }));

      const link = await screen.findByRole('link', { name: '+ Assign to Client' });
      expect(link.className).toBe(buttonClassName({ variant: 'primary', size: 'small' }));
    });

    it('withholds the action for a FORMER EDJEr, and says why', async () => {
      // The server refuses an assignment for an inactive EDJEr, so the control cannot succeed.
      renderPanel(
        stubFetchByUrl({
          [`GET ${DETAIL}`]: { status: 200, body: { ...DETAIL_BODY, isActive: false } },
        }),
      );

      await screen.findByText('Contoso');

      expect(screen.queryByRole('link', { name: '+ Assign to Client' })).not.toBeInTheDocument();
      expect(screen.getByText(/former edjer cannot be assigned/i)).toBeInTheDocument();
    });

    it('offers the action when `isActive` is ABSENT, rather than reading absence as inactive', async () => {
      // `isActive` is elevated-only and omitted for a baseline viewer, who cannot reach this screen.
      // So this pins `=== false` over falsy rather than a reachable case.
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

      expect(await screen.findByRole('link', { name: '+ Assign to Client' })).toBeInTheDocument();
    });

    it('offers no action at all while the record is still loading or failed to load', async () => {
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 403 } }));

      await screen.findByRole('alert');

      expect(screen.queryByRole('link', { name: '+ Assign to Client' })).not.toBeInTheDocument();
    });
  });

  describe('the SOW affordance (AC-20, gated by AC-16/FR-025)', () => {
    /** The same body, with the server having GRANTED the assignment read on every row. */
    const GRANTED = {
      ...DETAIL_BODY,
      assignmentHistory: DETAIL_BODY.assignmentHistory.map((assignment) => ({
        ...assignment,
        canViewAssignment: true,
      })),
    };

    it('is withheld when the server did not grant it — an entitlement, not a role check', async () => {
      // DETAIL_BODY's rows carry no `canViewAssignment`, which is what a withheld grant looks like on
      // the wire: omitted, not false.
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

      await screen.findByText('Contoso');

      expect(screen.queryByRole('link', { name: /view sows/i })).not.toBeInTheDocument();
    });

    it('links each granted row into that assignment, nested under this EDJEr', async () => {
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: GRANTED } }));

      const link = await screen.findByRole('link', { name: 'View SOWs for Contoso' });

      // The EMPLOYEE-nested route, not the client-nested twin.
      expect(link).toHaveAttribute('href', '/team-directory/7/assignments/1?from=admin');
      expect(link).toHaveTextContent('View SOWs');
    });

    it('wears the shared table-link style, with only wrapping composed on top', async () => {
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: GRANTED } }));

      const link = await screen.findByRole('link', { name: 'View SOWs for Contoso' });
      expect(link.className).toBe(`${tableLinkClass} whitespace-nowrap`);
    });

    it('names the client in each link, so a column of them stays distinguishable', async () => {
      // WCAG 2.5.3: the accessible name EXTENDS the visible "View SOWs" rather than replacing it, which
      // is what a voice-control user reading the screen needs.
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: GRANTED } }));

      const links = await screen.findAllByRole('link', { name: /^View SOWs for / });

      expect(links.map((link) => link.getAttribute('aria-label'))).toEqual([
        'View SOWs for Contoso',
        'View SOWs for Fabrikam',
      ]);
      for (const link of links) {
        expect(link).toHaveTextContent('View SOWs');
      }
    });

    it('says a withheld cell is unavailable rather than leaving it blank', async () => {
      // A blank cell reads as missing data to a sighted viewer and as nothing at all to a screen
      // reader; "Not available" is a statement about entitlement.
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: DETAIL_BODY } }));

      await screen.findByText('Contoso');

      expect(screen.getAllByText('Not available').length).toBe(
        DETAIL_BODY.assignmentHistory.length,
      );
    });
  });

  describe('an internal client has no contracts, so its row offers no link (issue #518)', () => {
    /**
     * Row 1 (Contoso) internal, row 2 (Fabrikam) not — and BOTH granted by the server.
     *
     * The mix is the point. An EDJEr on the beach between engagements has an internal allocation
     * sitting beside real ones, so this is decided one row at a time; a per-record rule would strip
     * the link from engagements that do have contracts.
     */
    const MIXED = {
      ...DETAIL_BODY,
      assignmentHistory: DETAIL_BODY.assignmentHistory.map((assignment) => ({
        ...assignment,
        canViewAssignment: true,
        isInternal: assignment.clientName === 'Contoso',
      })),
    };

    it('withholds the link on the internal row while keeping it on the external one', async () => {
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: MIXED } }));

      await screen.findByText('Contoso');

      expect(screen.queryByRole('link', { name: 'View SOWs for Contoso' })).not.toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'View SOWs for Fabrikam' })).toBeInTheDocument();
    });

    it('keeps the SOW column, because the external rows still need it', async () => {
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: MIXED } }));

      const table = await screen.findByRole('table', { name: /assignment history/i });
      const headers = within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent?.trim());

      expect(headers).toContain('SOW');
    });

    it('says "No contracts" rather than reusing the withheld wording', async () => {
      // "Not available" is a statement about ENTITLEMENT — the server declined to grant the read.
      // This cell is a statement about the CLIENT: the section exists and holds nothing for internal
      // work. Reusing one phrase for both would tell a screen-reader user the wrong thing.
      renderPanel(stubFetchByUrl({ [`GET ${DETAIL}`]: { status: 200, body: MIXED } }));

      await screen.findByText('Contoso');

      expect(screen.getByText('No contracts')).toBeInTheDocument();
      expect(screen.queryByText('Not available')).not.toBeInTheDocument();
    });
  });

  it('has no accessibility violations, action and SOW links included', async () => {
    const { container } = renderPanel(
      stubFetchByUrl({
        [`GET ${DETAIL}`]: {
          status: 200,
          body: {
            ...DETAIL_BODY,
            isActive: true,
            assignmentHistory: DETAIL_BODY.assignmentHistory.map((assignment) => ({
              ...assignment,
              canViewAssignment: true,
            })),
          },
        },
      }),
    );

    await screen.findByRole('link', { name: '+ Assign to Client' });

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });
});
