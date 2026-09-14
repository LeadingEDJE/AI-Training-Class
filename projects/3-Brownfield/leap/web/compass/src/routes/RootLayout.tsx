import { Outlet } from '@tanstack/react-router';
import { CompassNav } from '../components/CompassNav';

/**
 * The shell every Compass route renders inside: the role-gated navigation followed by the matched
 * route's own content.
 *
 * `CompassNav` is a SIBLING of `<Outlet />`, never a wrapper around it. That is the same
 * composition `main.tsx` used before this feature introduced a router, and it is load-bearing for
 * the same reason: `CompassNav` and the page it sits above both call `apiFetch`, and a test that
 * stubs `global.fetch` with one single-shot response would see the two calls collide if the page
 * were rendered *inside* the nav's subtree. Keeping them siblings lets each component's own test
 * render it alone.
 */
export function RootLayout() {
  return (
    <>
      <CompassNav />
      <Outlet />
    </>
  );
}
