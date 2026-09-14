import { render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import type { ReactNode } from 'react';
import { CompassNav } from '../../src/components/CompassNav';
import { checkAccessibility } from '../../src/lib/accessibility-check';
import { AdminLayout } from '../../src/features/admin/AdminLayout';
import { LookupAdminPage } from '../../src/features/lookups/LookupAdminPage';
import { EdjerListPage } from '../../src/features/edjers/EdjerListPage';
import { EdjerFormPage } from '../../src/features/edjers/EdjerFormPage';
import { TeamDirectoryPage } from '../../src/features/team-directory/TeamDirectoryPage';
import { ClientAssignmentHistory } from '../../src/features/clients/ClientAssignmentHistory';
import { NotFoundPage } from '../../src/routes/NotFoundPage';

/**
 * The US7 WCAG 2.1 AA baseline (#57): zero violations across every existing Compass screen.
 * `checkAccessibility` combines axe-core's structural ruleset (roles, labels, landmarks — reliable
 * under jsdom) with a dedicated inline-colour contrast walk (axe's own color-contrast rule needs
 * real layout/paint to judge element visibility, which jsdom doesn't provide). See
 * `web/compass/README.md` for the documented rationale.
 *
 * **Each screen is checked in its OWN render.** They used to be concatenated into one container,
 * which was fine while there was one page but breaks the moment a second screen exists: two pages in
 * one tree means two `<main>` landmarks, and axe correctly reports `landmark-no-duplicate-main` for
 * a document that can never occur — the router renders exactly one route at a time. Add a new screen
 * as a new case below, not as another sibling in a shared tree.
 */
vi.mock('../../src/hooks/useCurrentUser', () => ({
  useCurrentUser: () => ({
    data: {
      edjeId: 'e1',
      email: 'super@edje.test',
      displayName: 'Super Admin',
      privileges: ['Compass Super Admin'],
      impersonator: null,
    },
  }),
  currentUserQueryKey: ['current-user'],
}));

// The admin shell renders router primitives. Stubbing them keeps this file about accessibility
// rather than routing, and lets the shell be checked without mounting a whole router.
vi.mock('@tanstack/react-router', () => ({
  Outlet: () => <div />,
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => (
    <a href={to}>{children}</a>
  ),
  // The EDJEr form navigates on save and reads its id from the route. Both are stubbed so the screen can
  // be checked without a router; `useParams` returns nothing, which is the ADD form — the edit form's own
  // accessibility is the same tree with values in it.
  // TabNav reads the pathname to decide which area is current. Stubbed at the admin index so the
  // shell renders as it does at `/compass/admin`.
  useRouterState: ({ select }: { select: (state: unknown) => unknown }) =>
    select({ location: { pathname: '/admin' } }),
  useNavigate: () => () => undefined,
  useParams: () => ({}),
}));

function renderScreen(screen: ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{screen}</QueryClientProvider>);
}

describe('WCAG 2.1 AA baseline', () => {
  beforeEach(() => {
    // URL-aware, because a blanket single-object answer is not a faithful double: a COLLECTION endpoint
    // answering with one object reached a `.map` and crashed the EDJEr form's render — a white screen with
    // no error state. That fragility is guarded in the screen now too, but a double that cannot happen in
    // production should not be what proves it.
    vi.stubGlobal(
      'fetch',
      vi.fn((input: string | URL | Request) => {
        const url = typeof input === 'string' ? input : input.toString();
        const isCollection = /\/(edjers|employee-types|invoice-frequency-types)(\?|$)/.test(url);

        return Promise.resolve({
          status: 200,
          ok: true,
          json: async () =>
            isCollection ? [] : { id: 'e1', displayName: 'Ada Lovelace', isActive: true },
        } as Response);
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('has zero accessibility violations on the not-found screen', async () => {
    const { container } = renderScreen(
      <>
        <CompassNav />
        <NotFoundPage />
      </>,
    );

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has zero accessibility violations on the lookup administration screen', async () => {
    // The only screen this feature ships. AC-NFR-5 applies to admin configuration exactly as it does
    // to customer-facing pages, and the retired/active state must not read as colour alone.
    const { container, getByText } = renderScreen(
      <>
        <CompassNav />
        <LookupAdminPage />
      </>,
    );
    await waitFor(() => expect(getByText('Employee Types')).toBeInTheDocument());

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has zero accessibility violations on the administration shell', async () => {
    // AC-NFR-5 covers admin configuration screens too — accessibility is not a customer-facing-only
    // posture. Sections are passed explicitly so the area navigation is exercised with real links.
    const { container } = renderScreen(
      <>
        <CompassNav />
        <AdminLayout sections={[{ to: '/admin/lookups', label: 'Lookups' }]} />
      </>,
    );

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });
  it('has zero accessibility violations on the EDJEr administration list', async () => {
    const { container, getByRole } = renderScreen(
      <>
        <CompassNav />
        <EdjerListPage />
      </>,
    );
    await waitFor(() => expect(getByRole('heading', { name: 'EDJErs' })).toBeInTheDocument());

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has zero accessibility violations on the Team Directory', async () => {
    // The widest READ surface: eight columns, seven of them sort controls, four filter selects and two
    // kinds of in-row link. The sort headers matter most — the mockups make every `th` clickable, and a
    // cell with a click handler is reachable by nobody using a keyboard.
    const { container, getByRole } = renderScreen(
      <>
        <CompassNav />
        <TeamDirectoryPage
          rows={[
            {
              id: 1,
              firstName: 'Maya',
              lastName: 'Alvarez',
              hireDate: '2016-03-14',
              email: 'maya.alvarez@example.test',
              employeeType: 'Full Time',
              coach: 'Jordan Wells',
              state: 'OH',
              currentAssignments: [{ clientId: 1, clientName: 'Buckeye Mutual' }],
              isActive: false,
            },
          ]}
          isPending={false}
          isError={false}
          privileges={['Compass Super Admin']}
          scopeCount={87}
          employeeTypeOptions={['Full Time', 'Part Time']}
          stateOptions={['OH', 'TX']}
        />
      </>,
    );
    await waitFor(() =>
      expect(getByRole('heading', { name: 'Team Directory' })).toBeInTheDocument(),
    );

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has zero accessibility violations on the client assignment-history panel', async () => {
    // AC-24's panel (issue #224). Checked in its OWN render rather than through the client form, because
    // the router stub here returns no params — which is the ADD form, where the panel deliberately does
    // not mount. Populated on purpose: the interesting pairings are the two pill tones and the two link
    // treatments, and an empty history renders none of them.
    //
    // The pills are the reason this case matters. The mockup distinguishes `CC-3`'s statuses by COLOUR
    // (`pill green` / `pill red`), and its own pale-green pairing measures 4.33:1 against a 4.5:1
    // minimum. `StatusPill` carries the measured replacement and always renders a word; this asserts
    // that what actually shipped clears both axe and the contrast check.
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve({
          status: 200,
          ok: true,
          json: async () => ({
            id: 4,
            clientName: 'Buckeye Mutual',
            status: 'Active',
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
            ],
          }),
        } as Response),
      ),
    );

    const { container, getByRole } = renderScreen(<ClientAssignmentHistory clientId={4} />);
    await waitFor(() =>
      expect(getByRole('region', { name: 'EDJEr Assignment History' })).toBeInTheDocument(),
    );

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has zero accessibility violations on the EDJEr form', async () => {
    // The widest surface this feature ships: eleven fields, four switches and two labelled regions. The
    // switches matter most — the mockups draw them as divs, and a div cannot be reached or announced.
    const { container, getByRole } = renderScreen(
      <>
        <CompassNav />
        <EdjerFormPage />
      </>,
    );
    await waitFor(() => expect(getByRole('region', { name: 'Profile' })).toBeInTheDocument());

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });
});
