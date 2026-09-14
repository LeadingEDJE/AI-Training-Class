import { createMemoryHistory } from '@tanstack/react-router';
import { describe, expect, it } from 'vitest';
import { COMPASS_BASE_PATH, createCompassRouter, routeTree } from '../../../src/routes/router';
import { LookupAdminPage } from '../../../src/features/lookups/LookupAdminPage';
import { TeamDirectoryRoute } from '../../../src/features/team-directory/TeamDirectoryRoute';

/**
 * Route resolution for every path the code-based tree serves.
 *
 * These are ordinary unit tests over ordinary modules, which is the whole point of research D-4's
 * decision: file-based routing would have produced a generated `routeTree.gen.ts` that could only be
 * kept out of the 98% tier by an exclusion, and `web/compass` has none.
 *
 * Resolution is driven through `createMemoryHistory` rather than the browser's history so a test
 * never depends on the ambient URL. Entries carry the `/compass` prefix because that is what the
 * serving layer delivers; the router strips the basepath before matching.
 */
function resolve(pathname: string) {
  const router = createCompassRouter(
    createMemoryHistory({ initialEntries: [`${COMPASS_BASE_PATH}${pathname}`] }),
  );
  return router;
}

describe('the Compass router', () => {
  it('is mounted at the /compass basepath the serving layer delivers', () => {
    // The Vite `base`, the nginx `location /compass/` block and this value must agree — a mismatch
    // serves a blank page with a 404 on every asset.
    expect(COMPASS_BASE_PATH).toBe('/compass');
  });

  it('resolves the index route at the base path itself', () => {
    const router = resolve('/');

    expect(router.state.location.pathname).toBe('/');
    expect(router.matchRoutes(router.state.location).at(-1)?.routeId).toBe('/');
  });

  it('lands the base path on the Team Directory, not the walking-skeleton employee lookup (#248)', () => {
    // The base path used to render HomePage, which read an employeeId out of the query string and
    // called the single-employee endpoint directly -- a demo of the module boundary, never a
    // destination. The Team Directory is the real screen CompassNav's baseline link has advertised
    // since Stream 1, so the index route should render exactly that component.
    const router = resolve('/');

    expect(router.routesById['/'].options.component).toBe(TeamDirectoryRoute);
  });

  it('resolves the administration shell at the admin-config nav destination', () => {
    // CompassNav's admin-config key points at /compass/admin. If this stops resolving, the only
    // Super-Admin-gated nav entry lands on the not-found page.
    const router = resolve('/admin');

    expect(router.matchRoutes(router.state.location).map((match) => match.routeId)).toContain(
      '/admin',
    );
  });

  it('lands the bare admin path on Lookup Administration rather than a prompt', () => {
    // Owner request 2026-08-18: /compass/admin is a landing page, and for now that page is Lookups.
    // Asserted as the COMPONENT rather than by following a redirect, because rendering in place is
    // the choice — a redirect would rewrite the address bar and add a history entry, so a test that
    // accepted either would not be testing the requirement.
    const router = resolve('/admin');

    expect(router.routesById['/admin/'].options.component).toBe(LookupAdminPage);
    expect(router.routesById['/admin/lookups'].options.component).toBe(LookupAdminPage);
  });

  it('resolves the lookup administration screen', () => {
    const router = resolve('/admin/lookups');

    expect(router.matchRoutes(router.state.location).map((match) => match.routeId)).toContain(
      '/admin/lookups',
    );
  });

  it('nests lookup administration inside the admin shell so it inherits the layout', () => {
    // If it resolved as a sibling of /admin instead of a child, the screen would render without the
    // administration heading and area navigation, and nothing else would notice.
    const router = resolve('/admin/lookups');

    const matchedIds = router.matchRoutes(router.state.location).map((match) => match.routeId);
    expect(matchedIds).toContain('/admin');
    expect(matchedIds.indexOf('/admin')).toBeLessThan(matchedIds.indexOf('/admin/lookups'));
  });

  it('resolves the EDJEr administration list', () => {
    const router = resolve('/admin/edjers');

    expect(router.matchRoutes(router.state.location).map((match) => match.routeId)).toContain(
      '/admin/edjers',
    );
  });

  it('resolves the add form on its own path, not as an EDJEr called "new"', () => {
    // `/admin/edjers/new` must match the literal route rather than the `$edjerId` parameter, or adding an
    // EDJEr would try to LOAD one named "new" and render a failure.
    const router = resolve('/admin/edjers/new');

    const matchedIds = router.matchRoutes(router.state.location).map((match) => match.routeId);
    expect(matchedIds).toContain('/admin/edjers/new');
    expect(matchedIds).not.toContain('/admin/edjers/$edjerId');
  });

  it('resolves one EDJEr record by id', () => {
    const router = resolve('/admin/edjers/7');

    expect(router.matchRoutes(router.state.location).map((match) => match.routeId)).toContain(
      '/admin/edjers/$edjerId',
    );
  });

  it('nests every EDJEr screen inside the admin shell so they inherit the layout', () => {
    for (const path of ['/admin/edjers', '/admin/edjers/new', '/admin/edjers/7']) {
      const router = resolve(path);
      const matchedIds = router.matchRoutes(router.state.location).map((match) => match.routeId);

      expect(matchedIds, path).toContain('/admin');
      expect(matchedIds.indexOf('/admin'), path).toBeLessThan(matchedIds.length - 1);
    }
  });

  it('exposes a root route that renders the navigation shell around the outlet', () => {
    // The root route owns the layout, so every child route inherits the nav without repeating it.
    expect(routeTree.options.component).toBeDefined();
  });

  it('answers an unmatched Compass path with the not-found route rather than the index page', () => {
    // Before the router existed, every unmatched path silently rendered the index page. This used
    // /team-directory as its specimen; feature 005 made that a real route, so it needs one that is
    // still genuinely unmatched.
    const router = resolve('/not-a-real-page');

    const matched = router.matchRoutes(router.state.location);
    expect(matched.at(-1)?.routeId).not.toBe('/');
  });

  it.each([
    ['/team-directory', '/team-directory'],
    ['/team-directory/42', '/team-directory/$employeeId'],
    ['/client-directory', '/client-directory'],
    ['/client-directory/7', '/client-directory/$clientId'],
  ])('resolves %s to %s', (path, routeId) => {
    // Feature 005's read surfaces. Route resolution is asserted here rather than by rendering: each
    // screen's own suite covers what it renders; this file owns whether the path matches at all.
    const router = resolve(path);

    expect(router.matchRoutes(router.state.location).at(-1)?.routeId).toBe(routeId);
  });

  it.each([
    ['/team-directory/42/assignments/3', '/team-directory/$employeeId/assignments/$assignmentId'],
    ['/client-directory/7/assignments/3', '/client-directory/$clientId/assignments/$assignmentId'],
  ])('resolves %s to %s', (path, routeId) => {
    // Feature 006's entry-point pivot (AC-1, AC-2, owner direction 2026-08-14): the assignment
    // detail screen is nested off EITHER parent record, not a standalone /compass/assignments list.
    const router = resolve(path);

    expect(router.matchRoutes(router.state.location).at(-1)?.routeId).toBe(routeId);
  });

  it('there is no standalone /compass/assignments route (feature 006, superseded Q2)', () => {
    const router = resolve('/assignments');

    expect(router.matchRoutes(router.state.location).at(-1)?.routeId).not.toBe('/assignments');
  });

  it.each([
    ['/team-directory/42/assignments/new', '/team-directory/$employeeId/assignments/new'],
    ['/client-directory/7/assignments/new', '/client-directory/$clientId/assignments/new'],
  ])('resolves %s to %s', (path, routeId) => {
    // The "new assignment" entry points (AC-1, AC-2).
    const router = resolve(path);

    expect(router.matchRoutes(router.state.location).at(-1)?.routeId).toBe(routeId);
  });

  it('resolves the literal "new" path rather than treating it as an assignment id', () => {
    // Same defect adminEdjerNewRoute/adminEdjerRoute guards against: were "new" to match
    // $assignmentId instead, starting a new assignment would try to LOAD one named "new".
    const router = resolve('/team-directory/42/assignments/new');

    const matchedIds = router.matchRoutes(router.state.location).map((match) => match.routeId);
    expect(matchedIds).toContain('/team-directory/$employeeId/assignments/new');
    expect(matchedIds).not.toContain('/team-directory/$employeeId/assignments/$assignmentId');
  });

  describe('the assignment origin search parameter (#448)', () => {
    /**
     * This router's only search parameter, narrowing rather than rejecting: a breadcrumb is not a
     * permission, so an unknown value gets the directory trail rather than an error.
     */
    const ORIGIN_ROUTES = ['/team-directory/9/assignments/new', '/team-directory/9/assignments/3'];

    it('keeps `from=admin` on both EDJEr-nested assignment routes', () => {
      for (const path of ORIGIN_ROUTES) {
        const router = resolve(`${path}?from=admin`);
        const match = router.matchRoutes(router.state.location).at(-1);

        expect((match?.search as { from?: string }).from, path).toBe('admin');
      }
    });

    it('drops any other value to undefined, which the containers read as the directory', () => {
      for (const value of ['team-directory', 'ADMIN', '../admin', '']) {
        const router = resolve(`/team-directory/9/assignments/3?from=${value}`);
        const match = router.matchRoutes(router.state.location).at(-1);

        expect((match?.search as { from?: string }).from, value).toBeUndefined();
      }
    });

    it('resolves with no search parameter at all', () => {
      const router = resolve('/team-directory/9/assignments/3');
      const match = router.matchRoutes(router.state.location).at(-1);

      expect((match?.search as { from?: string }).from).toBeUndefined();
    });
  });
});
