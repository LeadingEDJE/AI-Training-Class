import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

const navigate = vi.fn();
let routeParams: { clientId?: string } = {};

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => navigate,
  useParams: () => routeParams,
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));

const { ClientFormPage } = await import('../../../../src/features/clients/ClientFormPage');

const CLIENTS = '/api/compass/v1/admin/clients';
const INVOICE_FREQUENCY_TYPES = '/api/compass/v1/admin/invoice-frequency-types';

/**
 * The client add/edit form.
 *
 * Field inventory comes from AC-21 and AC-22: a unique name, the two optional contract dates, the
 * internal-EDJE ("beach") indicator, an invoice-frequency default drawn from ACTIVE types only, and inline
 * management of the client's billable time categories.
 *
 * **The load-bearing assertion in this file is a negative one**: no active/inactive control renders, for
 * any role, anywhere on the screen. Client status is derived from assignments and is never stored or set
 * (FR-021, FR-035) — a control that appeared to set it would be lying about what it does, and FR-037
 * additionally forbids status from disabling any edit path.
 */
const FREQUENCY_TYPES = [
  { id: 1, typeName: 'Monthly', isActive: true },
  { id: 2, typeName: 'Weekly', isActive: true },
];

const EXISTING = {
  id: 7,
  clientName: 'Contoso',
  msaSignedDate: '2024-03-01',
  ndaSignedDate: '2024-02-14',
  isInternal: false,
  invoiceFrequencyTypeId: 1,
  billableTimeCategories: [
    { id: 11, categoryName: 'Development', isActive: true },
    { id: 12, categoryName: 'Support', isActive: false },
  ],
};

/** The reads every render of this form performs: active cadences and (on edit) the record. */
const CLIENT_VIEW = '/api/compass/client-directory';
const ME = '/api/me';

function baseResponders(): Parameters<typeof stubFetchByUrl>[0] {
  return {
    [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 200, body: FREQUENCY_TYPES },
    [`GET ${CLIENTS}/7`]: { status: 200, body: EXISTING },
    [`POST ${CLIENTS}`]: { status: 201, body: EXISTING },
    [`PUT ${CLIENTS}/7`]: { status: 200, body: EXISTING },
    [`POST ${CLIENTS}/7/billable-time-categories`]: {
      status: 201,
      body: { id: 13, categoryName: 'Testing', isActive: true },
    },
    [`PUT ${CLIENTS}/7/billable-time-categories/11`]: {
      status: 200,
      body: { id: 11, categoryName: 'Development', isActive: false },
    },

    // The AC-24 assignment-history panel this screen now renders on edit (issue #224). Stubbed with an
    // EMPTY history: this file is about the FORM, and a populated panel would put a second table and a
    // second set of links into every query here for no assertion's benefit. The panel's own behaviour
    // is ClientAssignmentHistory.test.tsx's subject.
    [`GET ${CLIENT_VIEW}/7`]: {
      status: 200,
      body: { id: 7, clientName: 'Buckeye Mutual', status: 'Active', assignmentHistory: [] },
    },

    // The panel reads the session to decide whether to offer the assign affordance. Answered with no
    // Compass role, so the affordance stays absent and the form's own assertions are undisturbed.
    [`GET ${ME}`]: {
      status: 200,
      body: {
        edjeId: 'e-1',
        email: 'someone@leadingedje.com',
        displayName: 'Someone',
        privileges: [],
        impersonator: null,
      },
    },
  };
}

function renderForm(stub: FetchByUrlStub) {
  vi.stubGlobal('fetch', stub.mock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <ClientFormPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  routeParams = {};
  navigate.mockClear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('ClientFormPage — adding', () => {
  it('renders no assignment-history panel, because a client being added has no id to read one for', async () => {
    // AC-24's panel is EDIT-only. Rendering it here would fire a read for `client-directory/null` and
    // show an error region on a form that is working correctly (issue #224).
    renderForm(stubFetchByUrl(baseResponders()));

    expect(await screen.findByLabelText(/client name/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: /edjer assignment history/i }),
    ).not.toBeInTheDocument();
  });

  it('names the screen as an addition', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    expect(
      await screen.findByRole('heading', { name: /add.*client/i, level: 1 }),
    ).toBeInTheDocument();
  });

  it('offers every AC-21 field', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    expect(await screen.findByLabelText(/client name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/msa signed/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/nda signed/i)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /internal/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/invoice frequency/i)).toBeInTheDocument();
  });

  it('offers only ACTIVE invoice frequency types, plus an explicit no-default option', async () => {
    // The default is optional (AC-22), so "none" has to be expressible — a select whose only options
    // are cadences cannot say "this client has no default".
    renderForm(stubFetchByUrl(baseResponders()));

    const select = await screen.findByLabelText(/invoice frequency/i);
    // The cadences come from their own query, so they land after the field itself does.
    await waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(3));
    const options = within(select).getAllByRole('option');

    expect(options.map((option) => option.textContent)).toEqual([
      expect.stringMatching(/no default|none/i),
      'Monthly',
      'Weekly',
    ]);
  });

  it('sends the captured client and navigates back on success', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await userEvent.type(await screen.findByLabelText(/client name/i), 'Fabrikam');
    await userEvent.click(screen.getByRole('switch', { name: /internal/i }));
    await userEvent.click(screen.getByRole('button', { name: /save|add client/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${CLIENTS}`)).toHaveLength(1));

    const body = stub.bodiesFor(`POST ${CLIENTS}`)[0] as Record<string, unknown>;
    expect(body.clientName).toBe('Fabrikam');
    expect(body.isInternal).toBe(true);
    expect(navigate).toHaveBeenCalled();
  });

  it('never sends a status member, because the server rejects one', async () => {
    // FR-021 is enforced server-side by rejecting an unmapped member outright. A form that sent one
    // would turn every save into a 400 — so the absence is asserted on the wire, not only in the UI.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await userEvent.type(await screen.findByLabelText(/client name/i), 'Fabrikam');
    await userEvent.click(screen.getByRole('button', { name: /save|add client/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${CLIENTS}`)).toHaveLength(1));

    const body = stub.bodiesFor(`POST ${CLIENTS}`)[0] as Record<string, unknown>;
    expect(body).not.toHaveProperty('isActive');
    expect(body).not.toHaveProperty('status');
  });

  it('surfaces a duplicate-name rejection inline, and keeps what was typed', async () => {
    // A rejection that clears the form makes the administrator retype work the server already saw.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`POST ${CLIENTS}`]: {
        status: 409,
        body: { message: "The client name 'Contoso' is already in use." },
      },
    });
    renderForm(stub);

    await userEvent.type(await screen.findByLabelText(/client name/i), 'Contoso');
    await userEvent.click(screen.getByRole('button', { name: /save|add client/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/already in use/i);
    expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('surfaces a server-side validation rejection inline', async () => {
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`POST ${CLIENTS}`]: {
        status: 400,
        body: { message: 'That invoice frequency is no longer selectable.' },
      },
    });
    renderForm(stub);

    await userEvent.type(await screen.findByLabelText(/client name/i), 'Fabrikam');
    await userEvent.click(screen.getByRole('button', { name: /save|add client/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/no longer selectable/i);
  });
});

describe('ClientFormPage — editing', () => {
  it('renders the assignment-history panel, which is where AC-24 puts it', async () => {
    // Mockup `CC-3` places the full-width history card on the client CONFIGURATION record, below the
    // two-column details row — not only on the read view (issue #224).
    renderForm(stubFetchByUrl(baseResponders()));

    expect(
      await screen.findByRole('region', { name: /edjer assignment history/i }),
    ).toBeInTheDocument();
  });

  beforeEach(() => {
    routeParams = { clientId: '7' };
  });

  it('names the screen as an edit and loads the record', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    expect(
      await screen.findByRole('heading', { name: /edit.*client/i, level: 1 }),
    ).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso'));
    expect(screen.getByLabelText(/msa signed/i)).toHaveValue('2024-03-01');
  });

  it('lists the client billable time categories, active and inactive', async () => {
    // Inactive ones must stay visible: an administrator who cannot see a retired category cannot
    // reactivate one, and FR-007 keeps the row rather than removing it.
    renderForm(stubFetchByUrl(baseResponders()));

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    // The CELL, not `getByText`: since #471 each name also appears in the switch's
    // visually-hidden label, so a bare text query legitimately finds two nodes.
    expect(within(categories).getByRole('cell', { name: 'Development' })).toBeInTheDocument();
    expect(within(categories).getByRole('cell', { name: 'Support' })).toBeInTheDocument();

    // #471: state reaches a sighted viewer through the switch's POSITION and the track colour, and
    // assistive technology through its accessible name -- never a per-row word, which is what the
    // Offered/Retired pill was. `Development` is seeded active, `Support` inactive.
    expect(within(categories).getByRole('switch', { name: /development/i })).toBeChecked();
    expect(within(categories).getByRole('switch', { name: /support/i })).not.toBeChecked();

    // The retired vocabulary is gone from the screen entirely, in both directions.
    expect(within(categories).queryByText('Offered')).not.toBeInTheDocument();
    expect(within(categories).queryByText('Retired')).not.toBeInTheDocument();
    expect(within(categories).queryByRole('button', { name: /deactivate|reactivate/i })).toBeNull();

    // "Active" appears ONCE, as the column header -- not repeated on every row. That is the whole
    // point of following the Lookup admin layout rather than relabelling the pill.
    expect(within(categories).getByRole('columnheader', { name: 'Active' })).toBeInTheDocument();
  });

  it('adds a billable time category against this client', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    await userEvent.type(within(categories).getByLabelText(/category name/i), 'Testing');
    await userEvent.click(within(categories).getByRole('button', { name: /add/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`POST ${CLIENTS}/7/billable-time-categories`)).toHaveLength(1),
    );

    const body = stub.bodiesFor(`POST ${CLIENTS}/7/billable-time-categories`)[0] as Record<
      string,
      unknown
    >;
    expect(body.categoryName).toBe('Testing');
  });

  it('deactivates a billable time category through an update, never a delete', async () => {
    // AC-23: categories are deactivated, not removed (Principle VIII). The method is the assertion.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    // #471: the control is a switch NAMED FOR ITS CATEGORY, matching the Lookup admin tables --
    // the column header carries the word "Active" and the cell carries a bare toggle. There is no
    // Deactivate/Reactivate button any more, and no per-row state word.
    await userEvent.click(within(categories).getByRole('switch', { name: /development/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${CLIENTS}/7/billable-time-categories/11`)).toHaveLength(1),
    );

    const body = stub.bodiesFor(`PUT ${CLIENTS}/7/billable-time-categories/11`)[0] as Record<
      string,
      unknown
    >;
    expect(body.isActive).toBe(false);
  });

  it('surfaces a duplicate category name inline', async () => {
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`POST ${CLIENTS}/7/billable-time-categories`]: {
        status: 409,
        body: { message: 'This client already offers a category with that name.' },
      },
    });
    renderForm(stub);

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    await userEvent.type(within(categories).getByLabelText(/category name/i), 'Development');
    await userEvent.click(within(categories).getByRole('button', { name: /add/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/already offers/i);
  });

  it('reactivates a retired category through the same update route', async () => {
    // The counterpart to deactivation, and the reason retired categories stay listed: an administrator
    // who cannot see one cannot bring it back.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`PUT ${CLIENTS}/7/billable-time-categories/12`]: {
        status: 200,
        body: { id: 12, categoryName: 'Support', isActive: true },
      },
    });
    renderForm(stub);

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    await userEvent.click(within(categories).getByRole('switch', { name: /support/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${CLIENTS}/7/billable-time-categories/12`)).toHaveLength(1),
    );

    const body = stub.bodiesFor(`PUT ${CLIENTS}/7/billable-time-categories/12`)[0] as Record<
      string,
      unknown
    >;
    expect(body.isActive).toBe(true);
  });

  it('surfaces a refused category update inline', async () => {
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`PUT ${CLIENTS}/7/billable-time-categories/11`]: { status: 403 },
    });
    renderForm(stub);

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    await userEvent.click(within(categories).getByRole('switch', { name: /development/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('renders no category rows for a client with none, while still allowing one to be added', async () => {
    // FR-022: zero categories is a normal state for a usable client, not a blocker — so the empty
    // state renders no list and no error, and the add-category control stays usable.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${CLIENTS}/7`]: { status: 200, body: { ...EXISTING, billableTimeCategories: [] } },
      }),
    );

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    expect(within(categories).queryByRole('listitem')).not.toBeInTheDocument();
    expect(within(categories).getByRole('button', { name: /add category/i })).toBeEnabled();
  });

  it('keeps a since-retired invoice default as an option rather than silently clearing it', async () => {
    // FR-007: retiring a lookup value must not rewrite the records using it. Without this option the
    // select would fall back to "No default" and the next save of any unrelated field would clear a
    // cadence nobody touched.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${CLIENTS}/7`]: {
          status: 200,
          body: { ...EXISTING, invoiceFrequencyTypeId: 99 },
        },
      }),
    );

    const select = await screen.findByLabelText(/invoice frequency/i);
    await waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(4));
    expect(within(select).getByRole('option', { name: /retired/i })).toBeInTheDocument();
    expect(select).toHaveValue('99');
  });

  it('NAMES the since-retired invoice default rather than labelling it generically', async () => {
    // Issue #277. Keeping the option (above) satisfies FR-007's data guarantee, but the label read
    // "Current cadence (retired, no longer offered)" and never said WHICH cadence — so the one screen
    // that edits the client could not tell an administrator what it was about to preserve.
    //
    // The retired cadence is supplied through the LOOKUP collection, not the record: `CompassClient`
    // has no name member, and the form resolves the stored id against the whole collection. Reading the
    // full list rather than the active-only slice is the fix, and it keeps the name out of the detail
    // payload — which is what lets `CompassEdjerDtoTests`' id-not-name rule stand unmodified.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${CLIENTS}/7`]: { status: 200, body: { ...EXISTING, invoiceFrequencyTypeId: 99 } },
        [`GET ${INVOICE_FREQUENCY_TYPES}`]: {
          status: 200,
          body: [...FREQUENCY_TYPES, { id: 99, typeName: 'Quarterly', isActive: false }],
        },
      }),
    );

    const select = await screen.findByLabelText(/invoice frequency/i);
    const retired = await within(select).findByRole('option', { name: /retired/i });
    expect(retired).toHaveTextContent(/quarterly/i);
    expect(select).toHaveValue('99');
  });

  it('falls back to the generic retired label when the cadence cannot be resolved', async () => {
    // The fallback must survive: when the collection does not contain the stored id at all, the option
    // still needs to exist and stay selected, or the next save clears a cadence nobody touched. This is
    // also the shape a stale cache produces, so it is not merely theoretical.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${CLIENTS}/7`]: { status: 200, body: { ...EXISTING, invoiceFrequencyTypeId: 99 } },
      }),
    );

    const select = await screen.findByLabelText(/invoice frequency/i);
    const retired = await within(select).findByRole('option', { name: /retired/i });
    expect(retired).toHaveTextContent(/current cadence/i);
    expect(select).toHaveValue('99');
  });

  it('reports a refused record read as a refusal', async () => {
    renderForm(stubFetchByUrl({ ...baseResponders(), [`GET ${CLIENTS}/7`]: { status: 403 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('reports a failed record read as a failure', async () => {
    renderForm(stubFetchByUrl({ ...baseResponders(), [`GET ${CLIENTS}/7`]: { status: 500 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded/i);
  });

  it('cancels back to the list without saving', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso'));
    await userEvent.click(screen.getByRole('button', { name: /cancel/i }));

    expect(navigate).toHaveBeenCalled();
    expect(stub.bodiesFor(`PUT ${CLIENTS}/7`)).toHaveLength(0);
  });
});

describe('ClientFormPage — FR-021: status is never settable', () => {
  /**
   * The requirement is stronger than "the field is not sent": AC-42 says no client-deactivation action
   * exists on any surface for any role. A control that rendered and then failed server-side would still
   * have told the administrator such an action exists.
   */
  it.each([
    ['adding', {}],
    ['editing', { clientId: '7' }],
  ])('renders no active/inactive control when %s', async (_mode, params) => {
    routeParams = params;
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByLabelText(/client name/i);

    expect(screen.queryByLabelText(/^active$/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /deactivate client/i })).not.toBeInTheDocument();

    // SCOPED TO THE CLIENT, not the page — and narrowed rather than weakened (#471).
    //
    // This assertion used to be `queryByRole('switch', { name: /active/i })` page-wide, which was
    // exact while the only Active/Inactive control that COULD appear here was a client-status one.
    // Since #471 each billable time category carries its own switch, whose accessible name ends in
    // "Active"/"Inactive" — so the page-wide form now fails on a control FR-021 says nothing about.
    // A category is not a client, and AC-42 forbids a client-deactivation action.
    //
    // Deleting the assertion would have thrown away the file's own load-bearing negative. Instead
    // every matching switch must live INSIDE the Billable Time Categories region; one anywhere else
    // is the regression this always existed to catch, and still fails.
    const categoryRegion = screen.queryByRole('region', { name: /billable time categories/i });
    // ANY element type, not just `switch`: a client-status control could arrive as a checkbox or
    // a select, and the page-wide `queryByLabelText(/inactive/i)` this replaces caught those too.
    const activeSwitches = screen.queryAllByLabelText(/active|inactive/i);

    for (const control of activeSwitches) {
      expect(
        categoryRegion,
        'an Active/Inactive switch rendered with no Billable Time Categories region to belong to, ' +
          'so it can only be a client-status control (FR-021, AC-42)',
      ).not.toBeNull();
      expect(
        categoryRegion,
        'an Active/Inactive switch rendered OUTSIDE the Billable Time Categories region. FR-021: a ' +
          "client's status is derived from its assignments and is never settable.",
      ).toContainElement(control);
    }
  });

  it('renders no client status anywhere on the form', async () => {
    // US3's payload carries no status at all — it is added to the DTO in US4. A form that displayed one
    // would be reading a field that does not exist yet, or computing a second answer of its own.
    routeParams = { clientId: '7' };
    renderForm(stubFetchByUrl(baseResponders()));

    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso'));

    // Same narrowing as above, for the same reason (#471): "Active" now appears once as the Billable
    // Time Categories column header. Every occurrence must be inside that region; one outside it
    // would be the form displaying a client status, which is what this test exists to forbid.
    const categoryRegion = await screen.findByRole('region', {
      name: /billable time categories/i,
    });

    for (const node of screen.queryAllByText(/^(active|inactive)$/i)) {
      expect(
        categoryRegion,
        'the word Active or Inactive rendered outside the Billable Time Categories region. A client ' +
          'status is DERIVED from its assignments and this form must not display one.',
      ).toContainElement(node);
    }
  });

  it('keeps every edit path available regardless of the client having no assignments', async () => {
    // FR-037/FR-022: a zero-assignment client is Inactive once US4 derives status, and nothing about
    // that may disable an edit. Asserted as "the controls are enabled", which is the shape a future
    // status-driven regression would break.
    routeParams = { clientId: '7' };
    renderForm(stubFetchByUrl(baseResponders()));

    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toBeEnabled());
    expect(screen.getByRole('switch', { name: /internal/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();

    const categories = await screen.findByRole('region', { name: /billable time categories/i });
    expect(within(categories).getByRole('button', { name: /add/i })).toBeEnabled();
  });
});

describe('ClientFormPage — edge cases the screen must not crash on', () => {
  it('renders the add form when the route id is not a number, rather than requesting /NaN', async () => {
    // Fails CLOSED, matching lib/employee-id.ts: the value is interpolated into a request path, so a
    // junk segment must not become a request for a record that cannot exist.
    routeParams = { clientId: 'not-an-id' };
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    expect(
      await screen.findByRole('heading', { name: /add.*client/i, level: 1 }),
    ).toBeInTheDocument();
    expect(stub.calls().some((call) => call.includes('NaN'))).toBe(false);
  });

  it('degrades to an empty cadence list when the lookup answers a non-list', async () => {
    // A 200 carrying the wrong shape otherwise reaches `.map` and blanks the screen with no error
    // state at all. Empty is the right degradation for a select: visible and recoverable.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 200, body: { notAnArray: true } },
      }),
    );

    const select = await screen.findByLabelText(/invoice frequency/i);
    expect(within(select).getAllByRole('option')).toHaveLength(1);
  });

  it('sends null rather than empty strings for the optional dates', async () => {
    // `""` against a nullable Postgres `date` is a 400 that names a field the administrator left blank
    // on purpose.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await userEvent.type(await screen.findByLabelText(/client name/i), 'Fabrikam');
    await userEvent.click(screen.getByRole('button', { name: /save|add client/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${CLIENTS}`)).toHaveLength(1));

    const body = stub.bodiesFor(`POST ${CLIENTS}`)[0] as Record<string, unknown>;
    expect(body.msaSignedDate).toBeNull();
    expect(body.ndaSignedDate).toBeNull();
    expect(body.invoiceFrequencyTypeId).toBeNull();
  });

  it('shows a loading state while the record is still in flight', async () => {
    routeParams = { clientId: '7' };
    // No responder for the record: the query stays pending, which is the state under test.
    renderForm(
      stubFetchByUrl({
        [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 200, body: FREQUENCY_TYPES },
      }),
    );

    expect(await screen.findByRole('status')).toHaveTextContent(/loading client/i);
  });
});

describe('ClientFormPage — saving an edit', () => {
  beforeEach(() => {
    routeParams = { clientId: '7' };
  });

  it('sends a PUT carrying the edited values', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso'));
    await userEvent.clear(screen.getByLabelText(/client name/i));
    await userEvent.type(screen.getByLabelText(/client name/i), 'Contoso Group');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => expect(stub.bodiesFor(`PUT ${CLIENTS}/7`)).toHaveLength(1));

    const body = stub.bodiesFor(`PUT ${CLIENTS}/7`)[0] as Record<string, unknown>;
    expect(body.clientName).toBe('Contoso Group');
    expect(body.msaSignedDate).toBe('2024-03-01');
    expect(body.invoiceFrequencyTypeId).toBe(1);
    expect(body).not.toHaveProperty('isActive');
  });

  it('loads a record whose optional fields are all null without inventing values for them', async () => {
    // The round trip that matters: null on the wire becomes an empty control, and an untouched empty
    // control becomes null again — never "" and never 0.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`GET ${CLIENTS}/7`]: {
        status: 200,
        body: {
          ...EXISTING,
          msaSignedDate: null,
          ndaSignedDate: null,
          invoiceFrequencyTypeId: null,
        },
      },
    });
    renderForm(stub);

    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso'));
    expect(screen.getByLabelText(/msa signed/i)).toHaveValue('');
    expect(screen.getByLabelText(/nda signed/i)).toHaveValue('');
    expect(screen.getByLabelText(/invoice frequency/i)).toHaveValue('');

    await userEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(stub.bodiesFor(`PUT ${CLIENTS}/7`)).toHaveLength(1));

    const body = stub.bodiesFor(`PUT ${CLIENTS}/7`)[0] as Record<string, unknown>;
    expect(body.msaSignedDate).toBeNull();
    expect(body.ndaSignedDate).toBeNull();
    expect(body.invoiceFrequencyTypeId).toBeNull();
  });

  it('accepts dates and a cadence typed into a previously empty record', async () => {
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`GET ${CLIENTS}/7`]: {
        status: 200,
        body: {
          ...EXISTING,
          msaSignedDate: null,
          ndaSignedDate: null,
          invoiceFrequencyTypeId: null,
        },
      },
    });
    renderForm(stub);

    await waitFor(() => expect(screen.getByLabelText(/client name/i)).toHaveValue('Contoso'));
    await userEvent.type(screen.getByLabelText(/msa signed/i), '2024-05-01');
    await userEvent.type(screen.getByLabelText(/nda signed/i), '2024-04-01');
    await userEvent.selectOptions(screen.getByLabelText(/invoice frequency/i), '2');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => expect(stub.bodiesFor(`PUT ${CLIENTS}/7`)).toHaveLength(1));

    const body = stub.bodiesFor(`PUT ${CLIENTS}/7`)[0] as Record<string, unknown>;
    expect(body.msaSignedDate).toBe('2024-05-01');
    expect(body.ndaSignedDate).toBe('2024-04-01');
    expect(body.invoiceFrequencyTypeId).toBe(2);
  });

  it('keeps the stored cadence selectable even when the lookup itself is refused', async () => {
    // A refused lookup must not blank the screen — and must not silently clear the client's default
    // either. With no active list to check against, the stored cadence looks retired, so the
    // grandfathering option (FR-007) is what preserves it: "No default" plus the current value.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 403 },
      }),
    );

    const select = await screen.findByLabelText(/invoice frequency/i);
    await waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(2));
    expect(select).toHaveValue('1');
    expect(within(select).getByRole('option', { name: /retired/i })).toBeInTheDocument();
  });
});
