/**
 * The Compass route tree, defined in CODE rather than generated from files.
 *
 * **Why code-based routes and not `@tanstack/router-plugin`** (feature 004, research D-4). File-based
 * routing emits a generated `routeTree.gen.ts`, which would need a coverage exclusion — and
 * `web/compass` holds the platform's 98% tier with ZERO exclusions, enforced as a ratchet by
 * `scripts/check-coverage.sh`. `main.tsx` previously cited exactly that generated file as the reason
 * Compass had no router at all. Code-based routes resolve the objection instead of overriding it:
 * every route below is an ordinary TypeScript module, importable and unit-testable, and it counts
 * toward the tier like any other file. Do not introduce the plugin.
 *
 * `basepath` matches the Vite `base` and the nginx `location /compass/` mount. A mismatch produces a
 * blank page with 404s on every asset, so the three must move together.
 */
import { createRootRoute, createRoute, createRouter } from '@tanstack/react-router';
import type { RouterHistory } from '@tanstack/react-router';
import { AdminLayout } from '../features/admin/AdminLayout';
import { ClientAssignmentRoute } from '../features/assignments/ClientAssignmentRoute';
import { EmployeeAssignmentRoute } from '../features/assignments/EmployeeAssignmentRoute';
import { NewClientAssignmentRoute } from '../features/assignments/NewClientAssignmentRoute';
import { NewEmployeeAssignmentRoute } from '../features/assignments/NewEmployeeAssignmentRoute';
import { ClientFormPage } from '../features/clients/ClientFormPage';
import { ClientListPage } from '../features/clients/ClientListPage';
import { EdjerFormPage } from '../features/edjers/EdjerFormPage';
import { EdjerListPage } from '../features/edjers/EdjerListPage';
import { LookupAdminPage } from '../features/lookups/LookupAdminPage';
import { TeamDirectoryRoute } from '../features/team-directory/TeamDirectoryRoute';
import { EmployeeDetailRoute } from '../features/employee-detail/EmployeeDetailRoute';
import { ClientDirectoryRoute } from '../features/client-directory/ClientDirectoryRoute';
import { ClientViewRoute } from '../features/client-view/ClientViewRoute';
import { SalesDashboardRoute } from '../features/sales-dashboard/SalesDashboardRoute';
import { ReportsLayout } from '../features/reports/ReportsLayout';
import { AssignmentDurationRoute } from '../features/reports/assignment-duration/AssignmentDurationRoute';
import { AssignmentStartRoute } from '../features/reports/assignment-start/AssignmentStartRoute';
import { AvailabilityReportRoute } from '../features/reports/availability/AvailabilityReportRoute';
import { SowExtensionRoute } from '../features/reports/sow-extension/SowExtensionRoute';
import { NotFoundPage } from './NotFoundPage';
import { RootLayout } from './RootLayout';

/** The path prefix Compass is served under, in dev and deployed alike. */
export const COMPASS_BASE_PATH = '/compass';

const rootRoute = createRootRoute({
  component: RootLayout,
  notFoundComponent: NotFoundPage,
});

/**
 * The Compass index route. Renders the Team Directory directly rather than the former
 * walking-skeleton `HomePage` — landing on `/compass/` used to call the single-employee endpoint
 * with an id read from the query string, which is a demonstration of the module boundary, not a
 * destination (issue #248). The Team Directory is the real screen `CompassNav`'s baseline link has
 * advertised since Stream 1, so the base path now renders exactly that.
 */
const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: TeamDirectoryRoute,
});

/**
 * The Compass administration shell, at `/compass/admin` — the destination of `CompassNav`'s
 * `admin-config` key. Every configuration surface is a child of this route.
 */
const adminRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/admin',
  component: AdminLayout,
});

/**
 * What `/compass/admin` renders on its own: the Lookup Administration screen (owner request
 * 2026-08-18).
 *
 * It renders the same component as `/compass/admin/lookups` rather than REDIRECTING to it, which is
 * the choice that matters here. A redirect would rewrite the address bar out from under an
 * administrator who typed `/compass/admin`, and it would put an extra entry in the history so the
 * back button appears not to work. Rendering in place leaves `/compass/admin` a real, linkable
 * address.
 *
 * **That cost no longer exists (2026-08-21).** It was accepted while the area navigation was a strip of
 * tinted pills driven by `activeProps`, which marks a link active on the router's own match — the bare
 * admin path matches this index route rather than `/admin/lookups`, so landing here highlighted nothing
 * while Lookup Administration was plainly on screen. The strip is now the shared `TabNav`, which
 * resolves the index pathname to the route the index actually renders (`ADMIN_INDEX_RENDERS`) and keeps
 * comparing exactly — the same shape `ReportsLayout` and `nav-identity.ts` already use. So the screen
 * renders in place AND its tab is current.
 *
 * This replaced an `AdminIndexPage` that rendered "Select a configuration area to begin.": a prompt
 * whose only content was an instruction to use the navigation immediately above it.
 */
const adminIndexRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/',
  component: LookupAdminPage,
});

/** Lookup administration — employee types and invoice frequency types (issue #58). */
const adminLookupsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/lookups',
  component: LookupAdminPage,
});

// ---------------------------------------------------------------- Read surfaces (feature 005, #47)

/** The Team Directory (AC-5 to AC-7) — the destination `CompassNav`'s baseline link has advertised since Stream 1. */
const teamDirectoryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory',
  component: TeamDirectoryRoute,
});

/**
 * The read-only employee detail (AC-8) — AC-7's destination from a directory row. Nested under the
 * directory's path so the URL reads as the journey does, and the id is parsed at the route boundary.
 */
const employeeDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory/$employeeId',
  component: EmployeeDetailRoute,
});

/**
 * Which screen the viewer came from, for the breadcrumb the destination builds. The two EDJEr-nested
 * assignment routes are reached from both the Team Directory and the EDJEr admin record by the same
 * path, so the origin has to travel in the URL.
 *
 * Narrows rather than rejects: anything but the literal `admin` is the directory origin. A breadcrumb
 * is not a permission, so a bad value should cost a wrong link, not an error.
 */
const validateAssignmentOrigin = (search: Record<string, unknown>) => ({
  from: search.from === 'admin' ? ('admin' as const) : undefined,
});

/**
 * The "new assignment" form, reached from an EDJEr's own record (AC-1, AC-2).
 *
 * A LITERAL path, listed before the parameterised `employeeAssignmentRoute` sibling below — the
 * same `adminEdjerNewRoute`/`adminEdjerRoute` ordering convention. Were `new` to match
 * `$assignmentId` instead, starting a new assignment would try to LOAD one named "new" and render a
 * load failure.
 */
const newEmployeeAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory/$employeeId/assignments/new',
  component: NewEmployeeAssignmentRoute,
  validateSearch: validateAssignmentOrigin,
});

/**
 * The assignment detail screen, reached from an EDJEr's own record (feature 006, AC-1, AC-2 —
 * "reachable from EDJEr or Client record", mockup screen 5). Nested under the employee it belongs to
 * rather than a standalone `/compass/assignments` list, by owner direction 2026-08-14 superseding
 * this feature's earlier standalone-route decision (spec 006 Q2/FR-055a). See the sibling
 * `clientAssignmentRoute` below — both render the same underlying assignment by
 * `$assignmentId`, differing only in the breadcrumb `AssignmentDetailPage` builds.
 */
const employeeAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory/$employeeId/assignments/$assignmentId',
  component: EmployeeAssignmentRoute,
  validateSearch: validateAssignmentOrigin,
});

/** The Client Directory (AC-12) — the second baseline nav destination. */
const clientDirectoryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory',
  component: ClientDirectoryRoute,
});

/** The client view (AC-13 to AC-16) — reached from all three of the other screens. */
const clientViewRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory/$clientId',
  component: ClientViewRoute,
});

/**
 * The "new assignment" form, reached from a Client's own record — see `newEmployeeAssignmentRoute`
 * for why this is a literal path listed before its parameterised sibling below.
 */
const newClientAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory/$clientId/assignments/new',
  component: NewClientAssignmentRoute,
});

/** The assignment detail screen, reached from a Client's own record — see `employeeAssignmentRoute`. */
const clientAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory/$clientId/assignments/$assignmentId',
  component: ClientAssignmentRoute,
});

// ---------------------------------------------------------------- Stream 5 (feature 007, #75)

/** The Sales Dashboard (AC-36, AC-37) — one of the two nav destinations `compass-nav-permissions.ts` already advertises. */
const salesDashboardRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/sales-dashboard',
  component: SalesDashboardRoute,
});

// --------------------------------------------------- EDJEr configuration (feature 004 US2, #59)
//
// Note the difference in parent from the read surfaces above: these hang off `adminRoute`, so they
// inherit the administration shell's heading and area navigation, while feature 005's screens are
// children of the root. That is not an inconsistency — a configuration surface belongs inside the
// admin shell and a directory does not.

/** EDJEr administration — the list an administrator finds a record from (issue #59). */
const adminEdjersRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/edjers',
  component: EdjerListPage,
});

/**
 * The add form.
 *
 * Declared as a LITERAL path, and listed before the parameterised sibling below. Were `new` to match
 * `$edjerId` instead, adding an EDJEr would try to LOAD one named "new" and render a load failure — a
 * defect that looks like a backend problem from the screen.
 */
const adminEdjerNewRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/edjers/new',
  component: EdjerFormPage,
});

/** One EDJEr's record, addressed by `compass.employee`'s `int` key — the boundary's published form. */
const adminEdjerRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/edjers/$edjerId',
  component: EdjerFormPage,
});

// --------------------------------------------------- Client configuration (feature 004 US3, #60)

/** Client administration — the list an administrator finds a record from (issue #60). */
const adminClientsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/clients',
  component: ClientListPage,
});

/**
 * The add form.
 *
 * A LITERAL path, listed before the parameterised sibling — see `adminEdjerNewRoute` for the defect
 * this ordering prevents (adding a client would otherwise try to LOAD one named "new").
 */
const adminClientNewRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/clients/new',
  component: ClientFormPage,
});

/**
 * One client's record.
 *
 * Note the path collision this does NOT have with feature 005's `/client-directory/$clientId`: that
 * one is a read-only view under the root route, this is configuration under the admin shell. Two
 * screens for the same entity at two paths is deliberate — they serve different roles and show
 * different things.
 */
const adminClientRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/clients/$clientId',
  component: ClientFormPage,
});

/** The whole tree. Every new screen adds its route here rather than to a generated file. */
// ---------------------------------------------------------------- Reports (feature 007, #76-#78)

/**
 * The reports shell at `/compass/reports`, following the `adminRoute` pattern above.
 *
 * **All three children are registered here, in US2, even though two are not built until US3 and US4.**
 * `ReportsLayout`'s tab strip is made of typed `<Link>`s and the `Register` interface at the bottom of
 * this file makes a `to` naming an unregistered route a TypeScript ERROR — so registering only
 * `availability` would fail `npm run build` at the Phase 4 checkpoint, or leave a one-tab tab strip
 * until Phase 6. T067 and T078 swap the two placeholder components for the real views; neither adds a
 * route.
 */
const reportsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/reports',
  component: ReportsLayout,
});

/**
 * What `/compass/reports` renders on its own: the Availability Report, the default report (owner request
 * 2026-08-19).
 *
 * It renders the same component as `/compass/reports/availability` rather than REDIRECTING to it,
 * matching `adminIndexRoute` above and for that route's reasons: a redirect rewrites the address bar out
 * from under someone who typed `/compass/reports`, and it adds a history entry so the back button appears
 * not to work. Rendering in place leaves `/compass/reports` a real, linkable address.
 *
 * **This replaced a redirect, which is what US2 shipped first.** The redirect was chosen to keep the tab
 * strip highlighted, because an exact-match active check leaves the bare path highlighting nothing.
 *
 * **That cost no longer exists (owner report 2026-08-19).** It was accepted when the only alternatives
 * on the table were a redirect or prefix-matching the active check; the third option is to resolve the
 * index pathname to the route the index actually renders and keep comparing exactly, which is what
 * `ReportsLayout`'s `INDEX_RENDERS` now does — the same shape `nav-identity.ts` had already applied for
 * `/compass/` rendering the Team Directory. So the report renders in place AND its tab is current, with
 * no redirect, no rewritten address bar and Back still working in one press.
 *
 * The earlier note claimed `adminIndexRoute` accepted the same cost. It did not, and the difference is
 * structural rather than a matter of care: `/compass/admin` is itself a `CompassNav` link target, so it
 * matches exactly on its own. Every reports TAB target is a CHILD of the reports index path, so none of
 * them can. That is why this strip was the only navigation in Compass showing the defect.
 *
 * It also replaced a `ReportsIndexPage` reading "Choose a report" — a prompt whose only content was an
 * instruction to use the tab strip immediately above it, exactly the page `adminIndexRoute` deleted.
 */
const reportsIndexRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/',
  component: AvailabilityReportRoute,
});

/** The Availability Report (AC-38, #76) — US2. */
const reportsAvailabilityRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/availability',
  component: AvailabilityReportRoute,
});

/** Client Assignment Duration (AC-39, #77) — US3. */
const reportsAssignmentDurationRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/assignment-duration',
  component: AssignmentDurationRoute,
});

/** Assignment Start lookup (AC-40, #78) — registered now, built at US4's T078. */
const reportsAssignmentStartRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/assignment-start',
  component: AssignmentStartRoute,
});

/** SOW Extension Report (issue #534) — the fourth report, added under this same tab strip. */
const reportsSowExtensionRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/sow-extension',
  component: SowExtensionRoute,
});

export const routeTree = rootRoute.addChildren([
  homeRoute,
  teamDirectoryRoute,
  employeeDetailRoute,
  newEmployeeAssignmentRoute,
  employeeAssignmentRoute,
  clientDirectoryRoute,
  clientViewRoute,
  newClientAssignmentRoute,
  clientAssignmentRoute,
  salesDashboardRoute,
  adminRoute.addChildren([
    adminIndexRoute,
    adminLookupsRoute,
    adminEdjersRoute,
    adminEdjerNewRoute,
    adminEdjerRoute,
    adminClientsRoute,
    adminClientNewRoute,
    adminClientRoute,
  ]),
  reportsRoute.addChildren([
    reportsIndexRoute,
    reportsAvailabilityRoute,
    reportsAssignmentDurationRoute,
    reportsAssignmentStartRoute,
    reportsSowExtensionRoute,
  ]),
]);

/**
 * Builds a router over {@link routeTree}.
 *
 * `history` is injectable so tests can drive route resolution with `createMemoryHistory` instead of
 * the browser's history — the application itself passes nothing and gets the browser default.
 */
export function createCompassRouter(history?: RouterHistory) {
  return createRouter({
    routeTree,
    basepath: COMPASS_BASE_PATH,
    defaultNotFoundComponent: NotFoundPage,
    ...(history ? { history } : {}),
  });
}

/** The application's router instance. */
export const compassRouter = createCompassRouter();

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createCompassRouter>;
  }
}
