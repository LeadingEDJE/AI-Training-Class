import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

// The layout's only collaborators are the router's outlet and the nav's current-user read. Both are
// stubbed so this test asserts composition and nothing else — the same approach
// CompassNav.test.tsx takes with the permissions module.
vi.mock('@tanstack/react-router', () => ({
  Outlet: () => <div data-testid="routed-outlet" />,
}));

vi.mock('../../../src/hooks/useCurrentUser', () => ({
  useCurrentUser: () => ({ data: null }),
  currentUserQueryKey: ['current-user'],
}));

const { RootLayout } = await import('../../../src/routes/RootLayout');

describe('RootLayout', () => {
  it('renders the navigation landmark and the routed outlet', () => {
    render(<RootLayout />);

    expect(screen.getByRole('navigation', { name: 'Compass navigation' })).toBeInTheDocument();
    expect(screen.getByTestId('routed-outlet')).toBeInTheDocument();
  });

  it('renders the outlet as a SIBLING of the navigation, never inside it', () => {
    // Load-bearing, not cosmetic: the nav and the page beneath it both call apiFetch, and a test
    // that stubs global.fetch with one single-shot response would see the two calls collide if the
    // page were rendered inside the nav's subtree. main.tsx carried this constraint before the
    // router existed; it lives here now.
    render(<RootLayout />);

    const nav = screen.getByRole('navigation', { name: 'Compass navigation' });
    const outlet = screen.getByTestId('routed-outlet');

    expect(nav.contains(outlet)).toBe(false);
    expect(nav.nextElementSibling).toBe(outlet);
  });
});
