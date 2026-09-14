/**
 * The Compass screen inventory the stranding sweep walks (feature 011, T006/T007).
 *
 * **Why a hand-written list with a gate, rather than a derived one.** `router.ts` declares 25
 * `createRoute` calls of which two (`adminRoute`, `reportsRoute`) are layout parents, so 23 are
 * addressable leaves — but a child's full URL exists only once the tree is composed, and the
 * parameterised segments need a real id to be navigable. Composing the router inside a Playwright spec
 * to recover those URLs was considered and rejected: the repository already has a working answer.
 * `tests/unit/wcag-sweep-coverage.test.ts` gates a hand-written list against `router.ts`'s `path:`
 * literals, requiring every route to be listed or waived **with the covering spec named**. This file is
 * the second consumer of that gate, not a second mechanism.
 *
 * So the list below is deliberately explicit, and the gate is what stops it rotting — which is the
 * failure this whole feature exists to correct.
 */
import { expect, type Page } from '@playwright/test';

/** What the stranding sweep needs to visit one screen. */
export interface CompassScreen {
  /** Stable key. Also the `SCROLL_REGION_FLOOR_AT_390` key and the failure-message label. */
  name: string;
  /** A navigable URL, with parameterised segments already resolved. */
  path: string;
  /**
   * Drives which remedy applies: `read` screens stack to cards below `md` (FR-006a); `admin` and
   * `directory` keep tables at every width (FR-006b) and are asserted to do so.
   */
  category: 'read' | 'directory' | 'form' | 'admin' | 'report' | 'error';
}

/** Ids resolved once from the API, so a click path cannot turn a navigation bug into a layout failure. */
export interface SeededIds {
  employeeId: number;
  /** First client in the directory — used wherever the internal/external distinction does not matter. */
  clientId: number;
  /**
   * A client with `isInternal === true`, or null if the seed has none.
   *
   * **This distinction is load-bearing, not a nicety.** `ClientViewPage` omits the `SOW` column when
   * `view.isInternal`, which makes the plain-text `End Date` the last column — and that is the only
   * reason the 20px clip on that screen strands anything. On an EXTERNAL client the trailing `SOW`
   * column carries a `View SOW` link, which sits past the visible edge and gives the region a keyboard
   * path, so Gate A correctly clears it. Asserting the external variant alone passes today and proves
   * nothing about the defect.
   */
  internalClientId: number | null;
  /** A client with `isInternal === false`, so both variants of the screen can be asserted. */
  externalClientId: number | null;
  assignmentId: number | null;
}

/**
 * Resolves the ids the parameterised routes need.
 *
 * **Lifted from `wcag-sweep.critical.spec.ts`'s `beforeAll` rather than reinvented** — same endpoints,
 * same "first row wins" choice, same fail-loud assertion when the seed is missing. Two specs resolving
 * the same ids two different ways is how they drift.
 */
export async function resolveSeededIds(page: Page): Promise<SeededIds> {
  const employees = await (await page.request.get('/api/compass/team-directory')).json();
  const clients = await (await page.request.get('/api/compass/client-directory')).json();
  const assignments = await (await page.request.get('/api/compass/assignments')).json();

  expect(
    Array.isArray(employees) && employees.length > 0,
    'the seeded Compass directory must be present — run `make e2e-stack-up`',
  ).toBe(true);
  expect(Array.isArray(clients) && clients.length > 0).toBe(true);

  // `isInternal` is on ClientViewDto, not on the directory row (which carries only id, name and
  // status), so classifying requires reading the views.
  //
  // **Scans the whole list, and an earlier version capping it at eight was a real bug.** The directory
  // is ordered ALPHABETICALLY, and the internal client is "Leading EDJE (Internal)" — position 17 of 29
  // in the e2e seed. The cap never reached it, `internalClientId` came back null, the screen was
  // silently omitted from the inventory, and the assertion that matters most failed with a confusing
  // "not in the screen inventory". Do not reintroduce a cap without checking the ordering.
  let internalClientId: number | null = null;
  let externalClientId: number | null = null;

  for (const row of clients) {
    if (internalClientId !== null && externalClientId !== null) {
      break;
    }
    const view = await (await page.request.get(`/api/compass/client-directory/${row.id}`)).json();
    if (view?.isInternal === true) {
      internalClientId ??= row.id;
    } else if (view?.isInternal === false) {
      externalClientId ??= row.id;
    }
  }

  // Fail here, naming the cause, rather than omitting a screen and letting the spec report a puzzling
  // "not in the screen inventory" three frames away. The internal variant is the one that exercises the
  // 20px clip — a run without it is a run that cannot see the highest-severity defect in this feature.
  expect(
    internalClientId,
    'no client with isInternal === true in the seeded directory. The internal-client variant of the ' +
      'client record is the ONLY one that strands (isInternal drops the SOW column, leaving plain-text ' +
      'End Date last), so without it this suite silently stops covering the defect it exists for.',
  ).not.toBeNull();
  expect(externalClientId, 'no external client in the seeded directory').not.toBeNull();

  return {
    employeeId: employees[0].id,
    clientId: clients[0].id,
    internalClientId,
    externalClientId,
    assignmentId: Array.isArray(assignments) && assignments.length > 0 ? assignments[0].id : null,
  };
}

/**
 * Every Compass screen the stranding sweep asserts: 22 leaf routes plus `NotFoundPage`.
 *
 * **`NotFoundPage` is here as an explicit entry and cannot be otherwise (T007).** It is registered as
 * `notFoundComponent` (`router.ts:44`) and `defaultNotFoundComponent` (`router.ts:372`) — not a route —
 * so neither composing nor scraping `router.ts` yields it. #84's axe sweep already carries it for the
 * same reason and with the reasoning worth repeating: *"a real destination rather than an error state:
 * `CompassNav` links to areas that may not be routed yet, and a person who lands here still has to be
 * able to read it."*
 *
 * The two assignment-detail routes render the SAME component through two mounts (via EDJEr and via
 * Client). Both are listed, because the mount is what differs and a layout defect could live in either
 * parent — the same call #84's sweep makes.
 */
export function compassScreens({
  employeeId,
  clientId,
  internalClientId,
  externalClientId,
  assignmentId,
}: SeededIds): CompassScreen[] {
  const screens: CompassScreen[] = [
    // Read surfaces
    { name: 'compass-index', path: '/compass/', category: 'read' },
    { name: 'team-directory', path: '/compass/team-directory', category: 'directory' },
    { name: 'employee-detail', path: `/compass/team-directory/${employeeId}`, category: 'read' },
    { name: 'client-directory', path: '/compass/client-directory', category: 'directory' },
    { name: 'client-view', path: `/compass/client-directory/${clientId}`, category: 'read' },
    { name: 'sales-dashboard', path: '/compass/sales-dashboard', category: 'read' },

    // Assignment forms — the two creation entry points (FR-055a: never a standalone nav destination)
    {
      name: 'new-assignment-via-edjer',
      path: `/compass/team-directory/${employeeId}/assignments/new`,
      category: 'form',
    },
    {
      name: 'new-assignment-via-client',
      path: `/compass/client-directory/${clientId}/assignments/new`,
      category: 'form',
    },

    // Admin configuration. AC-NFR-7 covers these explicitly, while noting the work is expected to be
    // done on a desktop — which is why FR-006b keeps them tables at every width.
    { name: 'admin-index', path: '/compass/admin', category: 'admin' },
    { name: 'admin-lookups', path: '/compass/admin/lookups', category: 'admin' },
    { name: 'admin-edjers', path: '/compass/admin/edjers', category: 'admin' },
    { name: 'admin-edjer-new', path: '/compass/admin/edjers/new', category: 'admin' },
    { name: 'admin-edjer-edit', path: `/compass/admin/edjers/${employeeId}`, category: 'admin' },
    { name: 'admin-clients', path: '/compass/admin/clients', category: 'admin' },
    { name: 'admin-client-new', path: '/compass/admin/clients/new', category: 'admin' },
    { name: 'admin-client-edit', path: `/compass/admin/clients/${clientId}`, category: 'admin' },

    // Reports
    { name: 'reports-index', path: '/compass/reports', category: 'report' },
    { name: 'reports-availability', path: '/compass/reports/availability', category: 'report' },
    {
      name: 'reports-assignment-duration',
      path: '/compass/reports/assignment-duration',
      category: 'report',
    },
    {
      name: 'reports-assignment-start',
      path: '/compass/reports/assignment-start',
      category: 'report',
    },
    {
      name: 'reports-sow-extension',
      path: '/compass/reports/sow-extension',
      category: 'report',
    },

    // T007 — not a route, so no route list derives it. See the docstring above.
    { name: 'not-found', path: '/compass/no-such-screen', category: 'error' },
  ];

  // Both client-view variants, when the seed has them. See `SeededIds.internalClientId` for why the
  // internal one is the variant that actually exercises the defect.
  if (internalClientId !== null) {
    screens.push({
      name: 'client-view-internal',
      path: `/compass/client-directory/${internalClientId}`,
      category: 'read',
    });
  }
  if (externalClientId !== null) {
    screens.push({
      name: 'client-view-external',
      path: `/compass/client-directory/${externalClientId}`,
      category: 'read',
    });
  }

  // The two assignment-detail mounts need a seeded assignment. Omitted rather than faked when the seed
  // has none: navigating to a non-existent assignment measures an error state, which is a different
  // screen wearing this one's name.
  if (assignmentId !== null) {
    screens.push(
      {
        name: 'assignment-detail-via-edjer',
        path: `/compass/team-directory/${employeeId}/assignments/${assignmentId}`,
        category: 'read',
      },
      {
        name: 'assignment-detail-via-client',
        path: `/compass/client-directory/${clientId}/assignments/${assignmentId}`,
        category: 'read',
      },
    );
  }

  return screens;
}

/**
 * The number of ENTRIES `compassScreens()` yields on a fully-seeded stack: **26**.
 *
 * Frozen so a screen silently dropped from `compassScreens` fails rather than shrinking the sweep
 * (Gate D — *a selector matching nothing PASSES*).
 *
 * **Entries, not routes** — the two differ and the arithmetic is worth writing down, because the first
 * version of this constant said 23 and was quietly wrong once the client-view variants landed:
 *
 * - **23 leaf routes** (25 `createRoute` calls minus `adminRoute` and `reportsRoute`, which are layout
 *   parents), one entry each ....................................................................  23
 * - **`NotFoundPage`**, which is `notFoundComponent` rather than a route, so no route list derives it +1
 * - **`/client-directory/$clientId` measured THREE ways** — the first client the directory returns,
 *   plus an explicitly internal and an explicitly external one. Only the internal variant strands
 *   (`isInternal` drops the SOW column and leaves plain-text End Date last), so the extra two are the
 *   difference between covering the defect and passing beside it ................................. +2
 *
 * Total: 26.
 *
 * **Went 25 → 26 with `reports-sow-extension` (issue #534).** Bumping this is the LAST step, not the
 * first: the number is a consequence of `compassScreens()` above, so read that list, then count.
 */
export const EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT = 26;

/** The five viewport widths `docs/nfr/NFR-catalog.md` P4 names, re-exported from the axe helper. */
export { VIEWPORTS } from './axe';
