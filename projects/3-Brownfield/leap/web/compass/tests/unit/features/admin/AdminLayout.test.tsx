import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

// The router is stubbed so these assert composition, not routing. Route resolution for the admin
// paths is router.test.ts's job, and the strip's own pathname logic is TabNav.test.tsx's.
vi.mock('@tanstack/react-router', () => ({
  Outlet: () => <div data-testid="routed-outlet" />,
  // Spreads the rest so `aria-current` reaches the DOM — the shell's area navigation is `TabNav` since
  // 2026-08-21, and it marks the current area that way.
  Link: ({ to, children, ...rest }: { to: string; children: React.ReactNode }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  useRouterState: ({ select }: { select: (state: unknown) => unknown }) =>
    select({ location: { pathname: '/admin' } }),
}));

const { AdminLayout } = await import('../../../../src/features/admin/AdminLayout');
const { ADMIN_SECTIONS } = await import('../../../../src/features/admin/admin-sections');
type AdminSection = (typeof ADMIN_SECTIONS)[number];

// Sections are injected so the link rendering is asserted against real entries. Asserting over
// ADMIN_SECTIONS while it is empty would be a test that iterates nothing and passes — the fail-open
// shape this repository has been bitten by eight times.
// REAL routes, and a strict SUBSET of ADMIN_SECTIONS. It used to include an invented
// `/admin/widgets`, which is what proved the prop was honoured rather than the default — but
// `AdminSection.to` is now typed against the route tree, so an unreachable section is a compile error
// and inventing one here contradicts the type the shell ships. Injection is proven instead by the
// COUNT: two links where the default would render three.
const SECTIONS = [
  { to: '/admin/lookups', label: 'Lookups' },
  { to: '/admin/clients', label: 'Clients' },
] satisfies AdminSection[];

describe('AdminLayout', () => {
  it('names the area so a Super Admin knows where they are', () => {
    render(<AdminLayout />);

    // Exact, not case-insensitive: the heading is Title Case by owner request (2026-08-18), and a
    // /i regex is what let "Compass administration" ship beside "Lookup Administration".
    expect(screen.getByRole('heading', { name: 'Compass Administration' })).toBeInTheDocument();
  });

  it('renders the child configuration area through the outlet', () => {
    render(<AdminLayout />);

    expect(screen.getByTestId('routed-outlet')).toBeInTheDocument();
  });

  it('carries no subtitle explaining the obvious (issue #408)', () => {
    // Shipped as a mockup `.note` reading "Reference data and configuration. Changes take effect
    // immediately." — an owner-requested removal, since it told an administrator nothing they didn't
    // already know from being on the screen.
    render(<AdminLayout />);

    expect(screen.queryByText(/reference data and configuration/i)).not.toBeInTheDocument();
  });

  it('offers a link to every configuration area', () => {
    render(<AdminLayout sections={SECTIONS} />);

    for (const section of SECTIONS) {
      expect(screen.getByRole('link', { name: section.label })).toHaveAttribute('href', section.to);
    }
    // The half that proves the PROP was used: the default holds three areas, so a shell ignoring
    // `sections` renders three links and every assertion above still passes.
    expect(screen.queryAllByRole('link')).toHaveLength(SECTIONS.length);
    expect(SECTIONS.length).toBeLessThan(ADMIN_SECTIONS.length);
  });

  it('renders the area navigation landmark even with no areas to offer', () => {
    // The landmark is unconditional so the layout does not shift as areas are added — the same
    // reasoning CompassNav applies to rendering its own <nav> with zero links.
    render(<AdminLayout sections={[]} />);

    expect(screen.getByRole('navigation', { name: /configuration areas/i })).toBeInTheDocument();
    expect(screen.queryAllByRole('link')).toHaveLength(0);
  });

  it('marks the area the bare admin path renders, so the strip is never blank', () => {
    // The router mock above puts the pathname at `/admin`, which renders Lookup Administration
    // WITHOUT redirecting to `/admin/lookups`. The tinted-pill strip this replaced used `activeProps`,
    // which matches on the router's own match and so highlighted nothing at all here — recorded in
    // `router.ts` as an accepted cosmetic gap. This is the composition half of closing it; the
    // substitution itself is `ADMIN_INDEX_RENDERS`, and `TabNav.test.tsx` covers its logic.
    render(<AdminLayout />);

    expect(screen.getByRole('link', { name: 'Lookups' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'EDJErs' })).not.toHaveAttribute('aria-current');
  });

  it('defaults to the real configuration areas rather than an empty list', () => {
    // Guards the injection above: a default of [] would make every assertion in this file pass
    // while the shipped shell offered nothing.
    render(<AdminLayout />);

    for (const section of ADMIN_SECTIONS) {
      expect(screen.getByRole('link', { name: section.label })).toHaveAttribute('href', section.to);
    }
    expect(screen.queryAllByRole('link')).toHaveLength(ADMIN_SECTIONS.length);
  });

  it.each([
    ['/admin/lookups', 'Lookups'],
    ['/admin/edjers', 'EDJErs'],
  ])('offers %s as a configuration area', (to, label) => {
    // Named explicitly, unlike the test above, which iterates ADMIN_SECTIONS and therefore adapts to
    // whatever it happens to contain. That adaptability is right for guarding the injection default and
    // wrong for guarding COVERAGE: a screen whose route exists but whose section entry was forgotten is
    // unreachable from the shell, and an iterating test would pass anyway.
    render(<AdminLayout />);

    expect(screen.getByRole('link', { name: label })).toHaveAttribute('href', to);
  });
});
