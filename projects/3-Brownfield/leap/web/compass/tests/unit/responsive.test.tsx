import { render } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import type { ReactNode } from 'react';
import { CompassNav } from '../../src/components/CompassNav';
import { checkResponsiveOverflow } from '../../src/lib/responsive-check';
import { AdminLayout } from '../../src/features/admin/AdminLayout';
import { LookupAdminPage } from '../../src/features/lookups/LookupAdminPage';
import { EdjerListPage } from '../../src/features/edjers/EdjerListPage';
import { EdjerFormPage } from '../../src/features/edjers/EdjerFormPage';
import { NotFoundPage } from '../../src/routes/NotFoundPage';
import { ClientAssignmentHistory } from '../../src/features/clients/ClientAssignmentHistory';

/**
 * The US7 responsive baseline (#57): no horizontal page-body overflow at the five required
 * viewport widths, across every existing Compass screen. jsdom has no real layout engine — every
 * element reports `clientWidth`/`scrollWidth` as 0 regardless of CSS, so a check based on those
 * properties can never be made to fail and would be worthless (the "a gate matching zero
 * files still passes" risk). `checkResponsiveOverflow` instead reads explicit inline
 * `width`/`min-width` pixel declarations, the one signal jsdom resolves faithfully without a
 * layout engine. Full real-browser verification at these breakpoints is Stream 6's job — this is
 * the Phase 7 baseline only.
 *
 * Screens are rendered one at a time, matching `accessibility.test.tsx` — see its header for why.
 */
const VIEWPORTS = [390, 767, 768, 1280, 1920];

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

// See accessibility.test.tsx — the admin shell renders router primitives, stubbed here so this file
// stays about layout.
vi.mock('@tanstack/react-router', () => ({
  Outlet: () => <div />,
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => (
    <a href={to}>{children}</a>
  ),
  // The EDJEr form navigates on save and reads its id from the route.
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

const SCREENS: [name: string, element: ReactNode][] = [
  [
    'the not-found screen',
    <>
      <CompassNav />
      <NotFoundPage />
    </>,
  ],
  [
    'the lookup administration screen',
    <>
      <CompassNav />
      <LookupAdminPage />
    </>,
  ],
  [
    'the administration shell',
    <>
      <CompassNav />
      <AdminLayout sections={[{ to: '/admin/lookups', label: 'Lookups' }]} />
    </>,
  ],
  [
    'the EDJEr administration list',
    <>
      <CompassNav />
      <EdjerListPage />
    </>,
  ],
  [
    'the EDJEr form',
    <>
      <CompassNav />
      <EdjerFormPage />
    </>,
  ],
  [
    // AC-24's history panel (issue #224). Five columns is the widest table Compass renders, and 390px is
    // where that has to hold: spec 010's Q2 requires the status column to stay present and reachable
    // rather than being dropped or collapsed. `Table` wraps itself in `overflow-x-auto`, so the table
    // scrolls inside its own container instead of pushing the PAGE sideways — which is exactly what this
    // check measures, and the reason four columns beat five.
    'the client assignment-history panel',
    <>
      <CompassNav />
      <ClientAssignmentHistory clientId={4} />
    </>,
  ],
];

describe('responsive layout — no horizontal overflow', () => {
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

  describe.each(SCREENS)('%s', (_name, element) => {
    it.each(VIEWPORTS)('has no element wider than the %ipx viewport', (width) => {
      const { container } = renderScreen(element);
      expect(checkResponsiveOverflow(container, width)).toEqual([]);
    });
  });
});
