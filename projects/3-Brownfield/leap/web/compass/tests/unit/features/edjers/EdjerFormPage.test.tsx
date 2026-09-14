import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

const navigate = vi.fn();
let routeParams: { edjerId?: string } = {};

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => navigate,
  useParams: () => routeParams,
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));

const { EdjerFormPage } = await import('../../../../src/features/edjers/EdjerFormPage');

const EDJERS = '/api/compass/v1/admin/edjers';
const EMPLOYEE_TYPES = '/api/compass/v1/admin/employee-types';
const TEAM_DIRECTORY = '/api/compass/team-directory';

/**
 * The EDJEr add/edit form.
 *
 * Field inventory, labels, order, section grouping and hint text come from the mockups' EDJEr
 * Configuration screen (`#s-edjer`). Three of its details deliberately do not ship, and each is asserted
 * here rather than left to review:
 *
 * - **Coach is optional.** The mockup marks it required with an asterisk; AC-17 says optional and the
 *   column is nullable. Where a mockup and an acceptance criterion disagree, the criterion wins — the
 *   mockups' own banner says so.
 * - **No Client Assignment History card in ADD mode.** The mockup shows one (`EC-3`). It shipped with
 *   issue #223 and renders on EDIT only, because an EDJEr being added has no id to read a history for.
 *   Assignment MANAGEMENT is still Stream 3's (A-6): open assignments appear here only in the 422
 *   rejection.
 * - **No `.anno` annotation chips.** Mockup scaffolding.
 */
const EMPLOYEE_TYPE_LIST = [
  { id: 1, typeName: 'Full Time', isActive: true },
  { id: 2, typeName: 'Part Time', isActive: true },
];

const EXISTING = {
  id: 7,
  firstName: 'Maya',
  lastName: 'Alvarez',
  hireDate: '2016-03-14',
  email: 'maya.alvarez@example.test',
  employeeTypeId: 1,
  coachEmployeeId: null,
  stateOfResidence: 'OH',
  timezone: 'America/Chicago',
  isDeliveryTeam: true,
  isActive: true,
  timesheetRequired: true,
  canSubmitUnder40: false,
  includeInPayroll: true,
};

const SUMMARIES = [
  {
    id: 3,
    firstName: 'Jordan',
    lastName: 'Wells',
    email: 'jordan.wells@example.test',
    employeeTypeName: 'Full Time',
    isActive: true,
  },
];

/** The reads every render of this form performs: active types, the coach list, and (on edit) the record. */
function baseResponders(): Parameters<typeof stubFetchByUrl>[0] {
  return {
    [`GET ${EMPLOYEE_TYPES}`]: { status: 200, body: EMPLOYEE_TYPE_LIST },
    [`GET ${EDJERS}`]: { status: 200, body: SUMMARIES },
    [`GET ${EDJERS}/7`]: { status: 200, body: EXISTING },

    // The AC-20 assignment-history panel this screen now renders on edit (issue #223). Stubbed with
    // an EMPTY history: this file is about the form, and a populated panel would put a second
    // role="alert"/table into every query here for no assertion's benefit. The panel's own behaviour
    // is EdjerAssignmentHistory.test.tsx's subject.
    [`GET ${TEAM_DIRECTORY}/7`]: {
      status: 200,
      body: { ...EXISTING, assignmentHistory: [] },
    },
    [`POST ${EDJERS}`]: { status: 201, body: EXISTING },
    [`PUT ${EDJERS}/7`]: { status: 200, body: EXISTING },
  };
}

function renderForm(stub: FetchByUrlStub) {
  vi.stubGlobal('fetch', stub.mock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  // The client is returned as well as the render result: a save must refresh every surface the new
  // EDJEr appears on (#448), and the only way to assert that is against the client that holds them.
  return {
    ...render(
      <QueryClientProvider client={queryClient}>
        <EdjerFormPage />
      </QueryClientProvider>,
    ),
    queryClient,
  };
}

beforeEach(() => {
  routeParams = {};
  navigate.mockClear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('EdjerFormPage — adding', () => {
  it('names the screen as an addition', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    expect(
      await screen.findByRole('heading', { name: /add.*edjer/i, level: 1 }),
    ).toBeInTheDocument();
  });

  it('groups the fields the way the mockups do', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    // Two cards, in this order: the profile, then the Super-Admin-only time-tracking settings.
    expect(await screen.findByRole('region', { name: /profile/i })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /time tracking settings/i })).toBeInTheDocument();
  });

  it('offers every AC-17 field, with the mockups own labels', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/hire date/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/email address/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/employee type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/coach/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/state of residence/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/timezone/i)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /status/i })).toBeInTheDocument();

    expect(screen.getByRole('switch', { name: /timesheet required/i })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /can submit/i })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /include in payroll/i })).toBeInTheDocument();
  });

  it('marks the required fields required, and leaves the coach optional (AC-17)', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    for (const label of [
      /first name/i,
      /last name/i,
      /hire date/i,
      /email address/i,
      /employee type/i,
      /state of residence/i,
      /timezone/i,
    ]) {
      expect(screen.getByLabelText(label)).toHaveAttribute('aria-required', 'true');
    }

    // The mockup marks Coach with an asterisk. The acceptance criterion wins.
    expect(screen.getByLabelText(/coach/i)).not.toHaveAttribute('aria-required', 'true');
  });

  it('offers ONLY active employee types, whatever the lookup collection contains', async () => {
    // AC-25 / FR-005. The server refuses an inactive id regardless, but the list must not offer one.
    //
    // This asserts the OUTCOME rather than the request shape. It used to check for `activeOnly=true` in
    // the URL, which stopped being the mechanism when issue #277 made the form read the whole collection
    // so it could NAME a retired classification. Filtering is now the form's own job, so a test that
    // watched the query string would have gone green while offering retired values — this one cannot.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EMPLOYEE_TYPES}`]: {
          status: 200,
          body: [...EMPLOYEE_TYPE_LIST, { id: 99, typeName: 'Contract', isActive: false }],
        },
      }),
    );

    await screen.findByRole('region', { name: /profile/i });

    const select = screen.getByLabelText(/employee type/i);
    // Awaited: the cadences arrive from their own query, after the card renders. Asserting
    // synchronously here passes on a fast machine and fails in CI.
    await within(select).findByRole('option', { name: 'Full Time' });
    expect(within(select).getByRole('option', { name: 'Part Time' })).toBeInTheDocument();
    expect(within(select).queryByRole('option', { name: /contract/i })).not.toBeInTheDocument();
  });

  it('offers the US states by name, and no territory', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    const states = screen.getByLabelText(/state of residence/i);
    expect(within(states).getByRole('option', { name: 'Ohio' })).toBeInTheDocument();
    expect(
      within(states).getByRole('option', { name: 'District of Columbia' }),
    ).toBeInTheDocument();
    expect(within(states).queryByRole('option', { name: /puerto rico/i })).not.toBeInTheDocument();
  });

  it('offers ONLY the six US timezones, by name, valued as IANA ids (FR-8.1b)', async () => {
    // The regression test issue #421 asks for. Two halves, and the second is the one that matters:
    // a worldwide IANA list would satisfy "shows Eastern" perfectly well, so the OPTION COUNT is
    // asserted rather than a handful of memberships.
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    const zones = within(screen.getByLabelText(/timezone/i));

    expect(zones.getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Select a timezone',
      'Eastern',
      'Central',
      'Mountain',
      'Pacific',
      'Hawaiian',
      'Alaskan',
    ]);

    // The stored value is the IANA id, never the label — 'Eastern' in the column would be the legacy
    // free-text shape FR-8.6 moved away from.
    expect(zones.getByRole('option', { name: 'Eastern' })).toHaveValue('America/New_York');
    expect(zones.getByRole('option', { name: 'Hawaiian' })).toHaveValue('Pacific/Honolulu');

    // Named explicitly because it is the failure the acceptance criterion is written against.
    expect(zones.queryByRole('option', { name: /london|utc|gmt/i })).not.toBeInTheDocument();
  });

  it('renders no hint text under the timezone field (issue #687)', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    expect(screen.queryByText(/used for ooto scheduling/i)).not.toBeInTheDocument();
  });

  it('starts with no timezone chosen, so Eastern is never assumed on the reader s behalf', async () => {
    // FR-8.1 makes it required. A pre-selected default would be a value the administrator never
    // picked — precisely the silent-Eastern behaviour issue #420 shipped transitionally and #421 ends.
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    expect(screen.getByLabelText(/timezone/i)).toHaveValue('');
  });

  it('offers existing EDJErs as coaches, plus an empty choice for none', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    // Awaited on the OPTION rather than on the card, because the coach list arrives from its own fetch
    // and the card renders before it. Awaiting a proxy and then asserting synchronously is how a test
    // passes on a fast machine and fails in CI.
    await screen.findByRole('option', { name: /wells/i });

    const coach = screen.getByLabelText(/coach/i);
    expect(within(coach).getByRole('option', { name: /wells/i })).toBeInTheDocument();
    // Optional means selectable-as-nothing, not merely un-asterisked.
    expect(within(coach).getByRole('option', { name: /no coach/i })).toBeInTheDocument();
  });

  it('does not offer a FORMER EDJEr as a coach (#400)', async () => {
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            ...SUMMARIES,
            {
              id: 9,
              firstName: 'Dana',
              lastName: 'Prior',
              email: 'dana.prior@example.test',
              employeeTypeName: 'Full Time',
              isActive: false,
            },
          ],
        },
      }),
    );

    await screen.findByRole('option', { name: /wells/i });

    const coach = screen.getByLabelText(/coach/i);
    expect(within(coach).queryByRole('option', { name: /prior/i })).not.toBeInTheDocument();
    expect(within(coach).getByRole('option', { name: /wells/i })).toBeInTheDocument();
  });

  it('defaults the time-tracking flags the way the mockups show them', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /time tracking settings/i });

    expect(screen.getByRole('switch', { name: /timesheet required/i })).toBeChecked();
    expect(screen.getByRole('switch', { name: /can submit/i })).not.toBeChecked();
    expect(screen.getByRole('switch', { name: /include in payroll/i })).toBeChecked();
  });

  it('offers the Delivery Team toggle in the Profile card, on by default (issue #502)', async () => {
    // In Profile, NOT in Time Tracking Settings. The three flags in that card are Super-Admin-only and
    // withheld from a lesser tier's payload; this one is published to every tier, so grouping it with
    // them would file it under an entitlement it does not share.
    renderForm(stubFetchByUrl(baseResponders()));

    const profile = within(await screen.findByRole('region', { name: /profile/i }));
    const toggle = profile.getByRole('switch', { name: /delivery team/i });

    expect(toggle).toBeChecked();
    expect(
      within(screen.getByRole('region', { name: /time tracking settings/i })).queryByRole(
        'switch',
        {
          name: /delivery team/i,
        },
      ),
    ).not.toBeInTheDocument();
  });

  it('sends the delivery-team flag ON for a new EDJEr, without the administrator touching it', async () => {
    // The default has to reach the wire, not merely render checked. The server also defaults it, so a
    // form that dropped the field would still store `true` and look right — this is what tells the two
    // apart.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '1');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${EDJERS}`)).toHaveLength(1));
    expect(stub.bodiesFor(`POST ${EDJERS}`)[0]).toMatchObject({ isDeliveryTeam: true });
  });

  it('sends the delivery-team flag OFF when it is turned off', async () => {
    // Its own key, not the Status toggle's. Two switches in one card is where a copy-paste sends both
    // at `isActive`, and nothing about the rendered form would look wrong.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '1');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');

    await userEvent.click(screen.getByRole('switch', { name: /delivery team/i }));
    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${EDJERS}`)).toHaveLength(1));
    expect(stub.bodiesFor(`POST ${EDJERS}`)[0]).toMatchObject({
      isDeliveryTeam: false,
      isActive: true,
    });
  });

  it('names the delivery-team toggle from its aria-label, and keeps it on the Tab path', async () => {
    // Toggle renders a <button role="switch"> with NO <label> element, so aria-label is the only thing
    // naming it. The exact string is the discriminator: computed from contents the name comes out
    // "Delivery TeamOn", because the accessible-name algorithm trims each text run before joining and
    // discards the space between two sibling elements.
    renderForm(stubFetchByUrl(baseResponders()));

    const toggle = await screen.findByRole('switch', { name: 'Delivery Team On' });
    expect(toggle).toHaveAttribute('aria-label', 'Delivery Team On');
    expect(screen.queryByText('Delivery Team', { selector: 'label' })).not.toBeInTheDocument();

    // A <button> is natively Tab-reachable in every engine, so this is cheap rather than a hedge
    // against the WebKit plain-anchor defects — those were <a href>, which is a different rule.
    let tabs = 0;
    while (document.activeElement !== toggle && tabs < 40) {
      await userEvent.tab();
      tabs += 1;
    }
    expect(tabs).toBeGreaterThan(0);
    expect(toggle).toHaveFocus();

    // Operable from the keyboard, and the name follows the state rather than going stale.
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('switch', { name: 'Delivery Team Off' })).toHaveFocus();
  });

  it('POSTs what was typed, and navigates to the NEW RECORD so the first assignment can be added', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '1');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');
    await userEvent.selectOptions(screen.getByLabelText(/timezone/i), 'America/Denver');

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${EDJERS}`)).toHaveLength(1));

    expect(stub.bodiesFor(`POST ${EDJERS}`)[0]).toMatchObject({
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada@example.test',
      employeeTypeId: 1,
      stateOfResidence: 'OH',
      // The IANA id the option carries, not the 'Mountain' the administrator read (FR-8.1).
      timezone: 'America/Denver',
      coachEmployeeId: null,
      isActive: true,
    });

    // The assignment affordances live on the record, so an ADD lands there rather than on the list.
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith({
        to: '/admin/edjers/$edjerId',
        params: { edjerId: '7' },
      }),
    );
  });

  it('refreshes every surface a new EDJEr appears on', async () => {
    // Invalidating only the admin list left three other caches without the EDJEr just created.
    const stub = stubFetchByUrl(baseResponders());
    const { queryClient } = renderForm(stub);
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '1');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(navigate).toHaveBeenCalled());

    const invalidated = invalidate.mock.calls.map(([argument]) =>
      JSON.stringify((argument as { queryKey: unknown[] }).queryKey),
    );

    expect(invalidated).toContain(JSON.stringify(['compass', 'edjers']));
    expect(invalidated).toContain(JSON.stringify(['compass', 'team-directory']));
    expect(invalidated).toContain(JSON.stringify(['compass', 'employee-detail']));
    expect(invalidated).toContain(JSON.stringify(['compass', 'edjer-pickers']));
  });

  it('sends the employee type as a NUMBER, not the string a select yields', async () => {
    // A select's value is always a string. Sending "1" where the API expects an int is the defect that
    // shows up as a 400 nobody can explain from the form.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '2');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');
    await userEvent.selectOptions(screen.getByLabelText(/coach/i), '3');

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${EDJERS}`)).toHaveLength(1));

    const sent = stub.bodiesFor(`POST ${EDJERS}`)[0] as Record<string, unknown>;
    expect(sent.employeeTypeId).toBe(2);
    expect(sent.coachEmployeeId).toBe(3);
  });

  it('does not navigate away when the save is rejected', async () => {
    // Losing what was typed because the server said no is the worst possible response to a rejection.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`POST ${EDJERS}`]: { status: 409, body: { message: 'That email is already used.' } },
    });
    renderForm(stub);

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '1');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/already used/i);
    expect(navigate).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/first name/i)).toHaveValue('Ada');
  });

  it('wires each time-tracking toggle to its OWN field', async () => {
    // Three switches with near-identical markup is where a copy-paste sends two of them at the same key.
    // Nothing about the rendered form would look wrong, and the record would simply carry the wrong
    // settings — so each is flipped from its default and all three are asserted in the submitted body.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByRole('region', { name: /time tracking settings/i });

    await userEvent.type(screen.getByLabelText(/first name/i), 'Ada');
    await userEvent.type(screen.getByLabelText(/last name/i), 'Lovelace');
    await userEvent.type(screen.getByLabelText(/hire date/i), '2020-01-06');
    await userEvent.type(screen.getByLabelText(/email address/i), 'ada@example.test');
    await userEvent.selectOptions(screen.getByLabelText(/employee type/i), '1');
    await userEvent.selectOptions(screen.getByLabelText(/state of residence/i), 'OH');

    // Defaults are on / off / on, so each click inverts one of them.
    await userEvent.click(screen.getByRole('switch', { name: /timesheet required/i }));
    await userEvent.click(screen.getByRole('switch', { name: /can submit/i }));
    await userEvent.click(screen.getByRole('switch', { name: /include in payroll/i }));

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`POST ${EDJERS}`)).toHaveLength(1));

    expect(stub.bodiesFor(`POST ${EDJERS}`)[0]).toMatchObject({
      timesheetRequired: false,
      canSubmitUnder40: true,
      includeInPayroll: false,
    });
  });

  it('renders without options rather than crashing when a lookup answers a non-list', async () => {
    // A 200 carrying the wrong shape used to reach `.map` and take the whole screen down — a white page
    // with no error state, found by the accessibility suite. Degrading to "no options" keeps the field
    // visible and the failure recoverable.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EMPLOYEE_TYPES}`]: { status: 200, body: { notAnArray: true } },
      }),
    );

    const select = await screen.findByLabelText(/employee type/i);

    expect(select).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: /select a type/i })).toBeInTheDocument();
    expect(within(select).queryByRole('option', { name: 'Full Time' })).not.toBeInTheDocument();
  });

  it('shows no Client Assignment History card, because an EDJEr being added has no history', async () => {
    // This absence is about ADD mode only. The panel itself shipped with issue #223 and renders on
    // EDIT — the reason it cannot render here is that there is no id yet to read a history for, not
    // that the card is out of scope. (That is what this comment used to say, and it stopped being true.)
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    expect(screen.queryByRole('region', { name: /assignment history/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /assign to client/i })).not.toBeInTheDocument();
  });

  it('offers a way out that does not save', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    await screen.findByRole('region', { name: /profile/i });

    await userEvent.click(screen.getByRole('button', { name: /cancel/i }));

    expect(navigate).toHaveBeenCalledWith({ to: '/admin/edjers' });
  });
});

describe('EdjerFormPage — editing', () => {
  beforeEach(() => {
    routeParams = { edjerId: '7' };
  });

  it('places the Client Assignment History between Time Tracking Settings and the actions', async () => {
    // Owner request 2026-08-18. It used to render AFTER the Save/Cancel row, which put a read-only
    // panel below the form's own controls.
    //
    // Asserted on DOCUMENT ORDER rather than on a class or a wrapper, because the requirement is about
    // what the viewer meets in sequence — and because both a CSS reorder and a DOM move would satisfy a
    // structural assertion while only one of them changes the tab order. `compareDocumentPosition` is
    // what actually distinguishes them.
    renderForm(stubFetchByUrl(baseResponders()));

    const settings = await screen.findByRole('region', { name: /time tracking settings/i });
    const history = await screen.findByRole('region', { name: 'Client Assignment History' });
    const save = screen.getByRole('button', { name: /save edjer/i });

    const follows = (earlier: Element, later: Element) =>
      Boolean(earlier.compareDocumentPosition(later) & Node.DOCUMENT_POSITION_FOLLOWING);

    expect(follows(settings, history), 'history must come after Time Tracking Settings').toBe(true);
    expect(follows(history, save), 'the Save button must come after the history').toBe(true);
  });

  it('keeps the history inside the form, so it does not break the single-column flow', async () => {
    // It carries links, never form controls, so being inside `form` cannot interfere with submission —
    // and moving it out would have been the easy way to satisfy the ordering above while splitting the
    // card stack in two. (#448 briefly put two dialogs, each with a form, inside this one; the owner
    // replaced them with navigation, so the original reasoning holds again.)
    renderForm(stubFetchByUrl(baseResponders()));

    const history = await screen.findByRole('region', { name: 'Client Assignment History' });

    expect(history.closest('form')).not.toBeNull();
  });

  it('carries the design source breadcrumb and status pill (FR-012)', async () => {
    // `#s-edjer` draws `Admin / EDJEr Configuration / Maya Alvarez` above the title and an `Active`
    // pill beside it. `PageHeader` has supported `breadcrumb` and `status` since Stream 2 — they were
    // simply never passed, which is the whole of this gap.
    renderForm(stubFetchByUrl(baseResponders()));

    const trail = await screen.findByRole('navigation', { name: /breadcrumb/i });
    expect(trail).toHaveTextContent('EDJEr Configuration');
    expect(trail).toHaveTextContent('Maya Alvarez');

    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('names the screen as an edit and loads the record', async () => {
    renderForm(stubFetchByUrl(baseResponders()));

    expect(await screen.findByDisplayValue('Maya')).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toHaveValue('Alvarez');
    expect(screen.getByLabelText(/email address/i)).toHaveValue('maya.alvarez@example.test');
    expect(screen.getByLabelText(/hire date/i)).toHaveValue('2016-03-14');
  });

  it('reflects the stored time-tracking flags rather than the add defaults', async () => {
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: {
          status: 200,
          body: { ...EXISTING, timesheetRequired: false, canSubmitUnder40: true },
        },
      }),
    );

    await screen.findByDisplayValue('Maya');

    expect(screen.getByRole('switch', { name: /timesheet required/i })).not.toBeChecked();
    expect(screen.getByRole('switch', { name: /can submit/i })).toBeChecked();
  });

  it('shows the stored coach as selected when the EDJEr has one', async () => {
    // The nullable half of the coach field. A record WITH a coach must arrive selected, or opening the
    // form and saving any other field would silently clear the coaching relationship.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, coachEmployeeId: 3 } },
      }),
    );

    await screen.findByDisplayValue('Maya');
    await screen.findByRole('option', { name: /wells/i });

    expect(screen.getByLabelText(/coach/i)).toHaveValue('3');
  });

  it('keeps an ALREADY-ASSIGNED former coach selectable and labelled (#400)', async () => {
    // Filtering former EDJErs out naively would leave this record's select with no matching option, so
    // the browser would display "No coach" while values.coachEmployeeId still held 9 and submitted it.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, coachEmployeeId: 9 } },
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            ...SUMMARIES,
            {
              id: 9,
              firstName: 'Dana',
              lastName: 'Prior',
              email: 'dana.prior@example.test',
              employeeTypeName: 'Full Time',
              isActive: false,
            },
          ],
        },
      }),
    );

    await screen.findByDisplayValue('Maya');
    await screen.findByRole('option', { name: /prior/i });

    const coach = screen.getByLabelText(/coach/i) as HTMLSelectElement;
    // Labelled, so the operator can see the assignment is stale.
    expect(within(coach).getByRole('option', { name: /prior \(former\)/i })).toBeInTheDocument();
    // What the control SHOWS is what a save would send.
    expect(coach).toHaveValue('9');
  });

  it('does not offer the EDJEr themselves as their own coach', async () => {
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}`]: {
          status: 200,
          body: [
            ...SUMMARIES,
            {
              id: 7,
              firstName: 'Maya',
              lastName: 'Alvarez',
              email: 'maya.alvarez@example.test',
              employeeTypeName: 'Full Time',
              isActive: true,
            },
          ],
        },
      }),
    );

    await screen.findByDisplayValue('Maya');
    await screen.findByRole('option', { name: /wells/i });

    const coach = screen.getByLabelText(/coach/i);
    expect(within(coach).queryByRole('option', { name: /alvarez/i })).not.toBeInTheDocument();
  });

  it('PUTs to the record it loaded', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByDisplayValue('Maya');

    await userEvent.clear(screen.getByLabelText(/first name/i));
    await userEvent.type(screen.getByLabelText(/first name/i), 'Augusta');
    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`PUT ${EDJERS}/7`)).toHaveLength(1));
    expect(stub.bodiesFor(`PUT ${EDJERS}/7`)[0]).toMatchObject({ firstName: 'Augusta' });

    // Only ADD lands on the record. Asserted because the other arm of that branch has its own test.
    await waitFor(() => expect(navigate).toHaveBeenCalledWith({ to: '/admin/edjers' }));
  });

  it('shows the stored timezone as selected, and sends it back unchanged', async () => {
    // The record is Central, which is neither the blank an unloaded form shows nor the Eastern a
    // fallback would produce — so both of those failures are visible rather than plausible.
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByDisplayValue('Maya');

    expect(screen.getByLabelText(/timezone/i)).toHaveValue('America/Chicago');

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`PUT ${EDJERS}/7`)).toHaveLength(1));
    expect(stub.bodiesFor(`PUT ${EDJERS}/7`)[0]).toMatchObject({ timezone: 'America/Chicago' });
  });

  it('shows a stored delivery-team correction and sends it back unchanged', async () => {
    // The failure this exists for: a form that renders the toggle but never reads the record puts it
    // back on, so the next save of any unrelated field silently returns the EDJEr to the delivery team.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, isDeliveryTeam: false } },
    });
    renderForm(stub);

    await screen.findByDisplayValue('Maya');

    expect(screen.getByRole('switch', { name: /delivery team/i })).not.toBeChecked();

    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`PUT ${EDJERS}/7`)).toHaveLength(1));
    expect(stub.bodiesFor(`PUT ${EDJERS}/7`)[0]).toMatchObject({ isDeliveryTeam: false });
  });

  it('re-sends the CHOSEN zone when the administrator changes it', async () => {
    const stub = stubFetchByUrl(baseResponders());
    renderForm(stub);

    await screen.findByDisplayValue('Maya');

    await userEvent.selectOptions(screen.getByLabelText(/timezone/i), 'Pacific/Honolulu');
    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await waitFor(() => expect(stub.bodiesFor(`PUT ${EDJERS}/7`)).toHaveLength(1));
    expect(stub.bodiesFor(`PUT ${EDJERS}/7`)[0]).toMatchObject({ timezone: 'Pacific/Honolulu' });
  });

  it('renders a zone outside the six as unchosen rather than offering a seventh option', async () => {
    // The column carries no CHECK, so a value outside the six is reachable — a widening rolled back,
    // or a write that bypassed this form. FR-8.1b forbids retaining it as an extra option the way the
    // employee-type select does, so it shows as unchosen and the administrator must pick. Visible and
    // correctable; the server refuses the save until they do.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, timezone: 'Europe/London' } },
      }),
    );

    await screen.findByDisplayValue('Maya');

    const zones = within(screen.getByLabelText(/timezone/i));
    expect(zones.getAllByRole('option')).toHaveLength(7); // the placeholder plus the six
    expect(zones.queryByRole('option', { name: /london/i })).not.toBeInTheDocument();
  });

  it('keeps a retired employee type selectable so an unrelated edit is not blocked (FR-007)', async () => {
    // The EDJEr is classified by a type that has since been retired, so the active-only list omits it.
    // Dropping the stored value would silently reclassify them on the next save of any other field.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, employeeTypeId: 99 } },
        [`GET ${EMPLOYEE_TYPES}`]: { status: 200, body: EMPLOYEE_TYPE_LIST },
      }),
    );

    await screen.findByDisplayValue('Maya');

    expect(screen.getByLabelText(/employee type/i)).toHaveValue('99');
  });

  it('NAMES the retired employee type rather than labelling it generically', async () => {
    // Issue #277. Keeping the option (above) satisfies FR-007's data guarantee, but the label read
    // "Current type (retired, no longer offered)" and never said WHICH type — so an administrator
    // editing this EDJEr could not tell what classification was being retained, and touching the
    // dropdown made it unrecoverable from the form.
    //
    // The retired type is supplied through the LOOKUP collection, not the record: the fix reads the
    // whole collection rather than the active-only slice, so the name is resolvable without adding a
    // member to CompassEdjerDto — which CompassEdjerDtoTests forbids.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, employeeTypeId: 99 } },
        [`GET ${EMPLOYEE_TYPES}`]: {
          status: 200,
          body: [...EMPLOYEE_TYPE_LIST, { id: 99, typeName: 'Contract', isActive: false }],
        },
      }),
    );

    await screen.findByDisplayValue('Maya');

    const select = screen.getByLabelText(/employee type/i);
    const retired = within(select).getByRole('option', { name: /retired/i });
    expect(retired).toHaveTextContent(/contract/i);
    expect(select).toHaveValue('99');
  });

  it('falls back to the generic retired label when the type cannot be resolved', async () => {
    // The fallback must survive: when the collection does not contain the stored id at all, the option
    // still has to exist and stay selected, or the next save reclassifies an EDJEr nobody meant to
    // touch. This is also the shape a stale cache produces, so it is not merely theoretical.
    renderForm(
      stubFetchByUrl({
        ...baseResponders(),
        [`GET ${EDJERS}/7`]: { status: 200, body: { ...EXISTING, employeeTypeId: 99 } },
        [`GET ${EMPLOYEE_TYPES}`]: { status: 200, body: EMPLOYEE_TYPE_LIST },
      }),
    );

    await screen.findByDisplayValue('Maya');

    const select = screen.getByLabelText(/employee type/i);
    const retired = within(select).getByRole('option', { name: /retired/i });
    expect(retired).toHaveTextContent(/current type/i);
    expect(select).toHaveValue('99');
  });

  // ------------------------------------------------- the 422 the deactivation guard answers

  it('renders a blocked deactivation with the assignments that must be end-dated', async () => {
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`PUT ${EDJERS}/7`]: {
        status: 422,
        body: {
          message: 'Maya Alvarez still holds 2 assignment(s) with no end date.',
          blockingAssignments: [
            {
              assignmentId: 42,
              clientId: 7,
              clientName: 'Buckeye Mutual',
              startDate: '2024-04-01',
            },
            {
              assignmentId: 43,
              clientId: 9,
              clientName: 'Olentangy Health',
              startDate: '2024-06-12',
            },
          ],
        },
      },
    });
    renderForm(stub);

    await screen.findByDisplayValue('Maya');

    await userEvent.click(screen.getByRole('switch', { name: /status/i }));
    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no end date/i);

    // The clients by name, because an assignment id is not actionable by a human.
    expect(screen.getByText(/buckeye mutual/i)).toBeInTheDocument();
    expect(screen.getByText(/olentangy health/i)).toBeInTheDocument();
  });

  it('does not imply Compass will end the assignments for them', async () => {
    // AC-19: an assignment's true end date frequently differs from the deactivation date, so nothing is
    // auto-ended. The recovery path needs Stream 3's surface and is deliberately absent here — so the copy
    // must not offer it.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`PUT ${EDJERS}/7`]: {
        status: 422,
        body: {
          message: 'Still holds assignments.',
          blockingAssignments: [
            {
              assignmentId: 42,
              clientId: 7,
              clientName: 'Buckeye Mutual',
              startDate: '2024-04-01',
            },
          ],
        },
      },
    });
    renderForm(stub);

    await screen.findByDisplayValue('Maya');
    await userEvent.click(screen.getByRole('switch', { name: /status/i }));
    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    const banner = await screen.findByRole('alert');

    // MM/DD/YYYY, like every other Compass surface — this list rendered the raw ISO date until the
    // AC-20 panel's identical defect (#280 review) prompted a sweep of the file. Nothing asserted the
    // rendered text before, which is exactly why it drifted.
    expect(banner).toHaveTextContent('04/01/2024');
    expect(banner).not.toHaveTextContent('2024-04-01');

    expect(
      screen.queryByRole('button', { name: /end (all )?assignment/i }),
    ).not.toBeInTheDocument();
  });

  it('leaves the EDJEr shown as active after a blocked deactivation', async () => {
    // The server refused, so the record is unchanged — a form still showing the toggle off would tell the
    // administrator the opposite of what happened.
    const stub = stubFetchByUrl({
      ...baseResponders(),
      [`PUT ${EDJERS}/7`]: {
        status: 422,
        body: { message: 'Still holds assignments.', blockingAssignments: [] },
      },
    });
    renderForm(stub);

    await screen.findByDisplayValue('Maya');
    await userEvent.click(screen.getByRole('switch', { name: /status/i }));
    await userEvent.click(screen.getByRole('button', { name: /save edjer/i }));

    await screen.findByRole('alert');

    expect(screen.getByRole('switch', { name: /status/i })).toBeChecked();
  });

  it('reports a record that could not be loaded', async () => {
    renderForm(stubFetchByUrl({ ...baseResponders(), [`GET ${EDJERS}/7`]: { status: 404 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded/i);
  });

  it('reports a refusal on the record read', async () => {
    renderForm(stubFetchByUrl({ ...baseResponders(), [`GET ${EDJERS}/7`]: { status: 403 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
  });
});
