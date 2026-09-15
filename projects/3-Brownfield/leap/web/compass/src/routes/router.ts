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

/** The path prefix Compass is served under in production; dev serves from the root. */
export const COMPASS_BASE_PATH = '/compass';

const rootRoute = createRootRoute({
  component: RootLayout,
  notFoundComponent: NotFoundPage,
});

/** The Compass index route, redirecting to the Team Directory. */
const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: TeamDirectoryRoute,
});

const adminRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/admin',
  component: AdminLayout,
});

/** What `/compass/admin` renders on its own: a redirect to `/compass/admin/lookups`. */
const adminIndexRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/',
  component: LookupAdminPage,
});

const adminLookupsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/lookups',
  component: LookupAdminPage,
});

const teamDirectoryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory',
  component: TeamDirectoryRoute,
});

const employeeDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory/$employeeId',
  component: EmployeeDetailRoute,
});

const validateAssignmentOrigin = (search: Record<string, unknown>) => ({
  from: search.from === 'admin' ? ('admin' as const) : undefined,
});

const newEmployeeAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory/$employeeId/assignments/new',
  component: NewEmployeeAssignmentRoute,
  validateSearch: validateAssignmentOrigin,
});

const employeeAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/team-directory/$employeeId/assignments/$assignmentId',
  component: EmployeeAssignmentRoute,
  validateSearch: validateAssignmentOrigin,
});

const clientDirectoryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory',
  component: ClientDirectoryRoute,
});

const clientViewRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory/$clientId',
  component: ClientViewRoute,
});

const newClientAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory/$clientId/assignments/new',
  component: NewClientAssignmentRoute,
});

const clientAssignmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/client-directory/$clientId/assignments/$assignmentId',
  component: ClientAssignmentRoute,
});

const salesDashboardRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/sales-dashboard',
  component: SalesDashboardRoute,
});

const adminEdjersRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/edjers',
  component: EdjerListPage,
});

const adminEdjerNewRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/edjers/new',
  component: EdjerFormPage,
});

const adminEdjerRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/edjers/$edjerId',
  component: EdjerFormPage,
});

const adminClientsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/clients',
  component: ClientListPage,
});

const adminClientNewRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/clients/new',
  component: ClientFormPage,
});

const adminClientRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/clients/$clientId',
  component: ClientFormPage,
});

const reportsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/reports',
  component: ReportsLayout,
});

/** What `/compass/reports` renders on its own: a redirect to `/compass/reports/availability`. */
const reportsIndexRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/',
  component: AvailabilityReportRoute,
});

const reportsAvailabilityRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/availability',
  component: AvailabilityReportRoute,
});

const reportsAssignmentDurationRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/assignment-duration',
  component: AssignmentDurationRoute,
});

const reportsAssignmentStartRoute = createRoute({
  getParentRoute: () => reportsRoute,
  path: '/assignment-start',
  component: AssignmentStartRoute,
});

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

export function createCompassRouter(history?: RouterHistory) {
  return createRouter({
    routeTree,
    basepath: COMPASS_BASE_PATH,
    defaultNotFoundComponent: NotFoundPage,
    ...(history ? { history } : {}),
  });
}

export const compassRouter = createCompassRouter();

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createCompassRouter>;
  }
}
