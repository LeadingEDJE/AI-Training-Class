// AC-NFR-5 / FR-085 — "Every screen MUST meet WCAG 2.1 AA, administrative configuration included."
// Issue #84.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';
import { VIEWPORTS, expectNoWcagViolations } from './helpers/axe';

/**
 * The per-screen budget for a sweep.
 *
 * **Every sweep here scans each screen at EVERY viewport**, so one screen costs one navigation plus
 * `VIEWPORTS.length` full axe runs -- and an axe run over a real page is seconds, not milliseconds.
 * Playwright's default is 30 s per TEST, which the eight-screen sweep below blows through on
 * arithmetic alone: eight navigations and forty axe runs do not fit, and the test then fails as a
 * bare `Test timeout of 30000ms exceeded` naming no violation, having asserted nothing about
 * accessibility at all. That is exactly how it failed on `main` (firefox and iphone, 2026-08-26).
 *
 * Derived from `screens.length` at each call site rather than hard-coded, so adding a screen to a
 * sweep grows its budget instead of quietly pushing it over the edge -- which is the failure this
 * constant exists to stop repeating.
 */
const SCREEN_SCAN_ALLOWANCE_MS = 2_000 + VIEWPORTS.length * 2_500;

/**
 * The extra budget a screen with an {@link Screen.open} step needs, on top of the scan allowance.
 *
 * `SCREEN_SCAN_ALLOWANCE_MS` above models one navigation plus the axe runs and nothing else, so a
 * screen that also drives an interaction before scanning costs more than its share. That interaction's
 * own worst case is its 15s visibility wait plus the click, which is what this covers. Without it the
 * sweep can exceed its timeout and fail as a bare "Test timeout exceeded" naming no violation — the
 * failure the allowance above exists to prevent.
 */
const INTERACTION_ALLOWANCE_MS = 16_000;

/** The whole sweep's budget: per-screen scan cost, plus the interaction cost where there is one. */
function budgetFor(screens: Screen[]): number {
  return screens.reduce(
    (ms, screen) => ms + SCREEN_SCAN_ALLOWANCE_MS + (screen.open ? INTERACTION_ALLOWANCE_MS : 0),
    0,
  );
}

/**
 * The sweep signs in as the Compass root, because AC-NFR-5 covers administrative configuration too and
 * no narrower role can reach those screens. That is a coverage decision, not a permissions statement —
 * the role matrix is asserted by `compass-nav-role-matrix`, not here.
 */
async function signInAsCompassSuperAdmin(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=wcag.sweep@example.test&groups=Compass-SuperAdmin-dev',
    { maxRedirects: 0 },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

/** One screen to scan: where it is, and how to know it has finished rendering. */
interface Screen {
  name: string;
  path: string;
  /** Awaited before scanning. Scanning a spinner measures the spinner. */
  ready: (page: Page) => Promise<void>;
  /**
   * An interaction to perform after `ready` and before scanning — for a state that is not a URL.
   * `docs/TEST-STRATEGY.md` names a modal as the interaction state that earns a browser sweep, since
   * jsdom cannot evaluate computed contrast, real layout or scroll geometry.
   */
  open?: (page: Page) => Promise<void>;
}

function heading(name: string, options: { exact?: boolean } = {}) {
  return async (page: Page) => {
    await expect(
      page.getByRole('heading', { name, exact: options.exact ?? false }).first(),
    ).toBeVisible({ timeout: 15_000 });
  };
}

test.describe.configure({ mode: 'serial' });

test.describe('WCAG 2.1 AA sweep [critical]', () => {
  // Ids for the parameterised routes, resolved once from the API rather than by clicking through the
  // UI: this spec is about the rendered result, and a click path would make an unrelated navigation
  // regression look like an accessibility failure.
  let employeeId: number;
  let clientId: number;
  let assignmentId: number | null = null;

  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage();
    await signInAsCompassSuperAdmin(page);

    const employees = await (await page.request.get('/api/compass/team-directory')).json();
    const clients = await (await page.request.get('/api/compass/client-directory')).json();
    const assignments = await (await page.request.get('/api/compass/assignments')).json();

    expect(
      Array.isArray(employees) && employees.length > 0,
      'the seeded Compass directory must be present — run `make e2e-stack-up`',
    ).toBe(true);
    expect(Array.isArray(clients) && clients.length > 0).toBe(true);

    employeeId = employees[0].id;
    clientId = clients[0].id;
    // A NON-INTERNAL assignment, deliberately -- not `assignments[0]` (issue #518). This suite scans
    // the "SOWs / Contracts" heading and clicks "+ Add SOW / Contract", and NEITHER exists for an
    // internal ("beach") client: internal work has no statements of work, so the card is not
    // rendered at all. `GET /api/compass/assignments` applies no ORDER BY, and the seeder's first
    // list element (assignment 1) is on the internal client -- so `[0]` timed out on a ready-locator
    // for a card that will never appear, and even where it happened to land on an external row the
    // outcome depended on which row Postgres returned first. Do not "simplify" this back to `[0]`.
    const external = Array.isArray(assignments)
      ? assignments.find((a: { id: number; isInternal?: boolean }) => a.isInternal !== true)
      : undefined;
    assignmentId = external !== undefined ? external.id : null;

    await page.close();
  });

  test.beforeEach(async ({ page }) => {
    await signInAsCompassSuperAdmin(page);
  });

  /**
   * The read surfaces and the reports shell.
   *
   * The three report children already carry their own sweeps in their own specs and are not repeated
   * here — duplicating them would double the runtime to re-assert what a per-screen spec already owns.
   * The reports INDEX is included, because nothing else scans it.
   */
  test('read surfaces have no WCAG 2.1 AA violations at any viewport', async ({ page }) => {
    const screens: Screen[] = [
      { name: 'Compass index', path: '/compass/', ready: heading('Team Directory') },
      { name: 'Team Directory', path: '/compass/team-directory', ready: heading('Team Directory') },
      {
        name: 'Employee detail',
        path: `/compass/team-directory/${employeeId}`,
        ready: heading('Assignment History'),
      },
      {
        name: 'Client Directory',
        path: '/compass/client-directory',
        ready: heading('Client Directory'),
      },
      {
        name: 'Client view',
        path: `/compass/client-directory/${clientId}`,
        ready: heading('Client Details'),
      },
      { name: 'Reports index', path: '/compass/reports', ready: heading('Reports') },
    ];

    // Derived from the real screens -- see SCREEN_SCAN_ALLOWANCE_MS and INTERACTION_ALLOWANCE_MS.
    test.setTimeout(budgetFor(screens));

    expect(await sweep(page, screens), 'read surfaces scanned').toBe(6);
  });

  /**
   * The write surfaces reached from a record (feature 006's entry-point pivot).
   *
   * A form is where accessibility most often breaks — labels, required markers, and the association
   * between a field and its validation message — and none of these screens was covered before.
   */
  test('assignment write surfaces have no WCAG 2.1 AA violations at any viewport', async ({
    page,
  }) => {
    const screens: Screen[] = [
      {
        name: 'New assignment (via EDJEr)',
        path: `/compass/team-directory/${employeeId}/assignments/new`,
        ready: heading('New Assignment'),
      },
      {
        name: 'New assignment (via Client)',
        path: `/compass/client-directory/${clientId}/assignments/new`,
        ready: heading('New Assignment'),
      },
    ];

    if (assignmentId !== null) {
      // BOTH mounts. The two routes render the same assignment through the same component and differ
      // only in the breadcrumb, which is exactly why the client-side one was missing here until the
      // coverage gate was tightened: it looks like a duplicate and is a distinct reachable screen.
      screens.push({
        name: 'Assignment detail (via EDJEr)',
        path: `/compass/team-directory/${employeeId}/assignments/${assignmentId}`,
        ready: heading('Client Assignment'),
      });
      screens.push({
        name: 'Assignment detail (via Client)',
        path: `/compass/client-directory/${clientId}/assignments/${assignmentId}`,
        ready: heading('Client Assignment'),
      });
      screens.push({
        // The only modal in Compass that no browser sweep opened.
        name: 'Assignment detail — Add SOW / Contract dialog',
        path: `/compass/team-directory/${employeeId}/assignments/${assignmentId}`,
        ready: heading('SOWs / Contracts'),
        open: async (page) => {
          await page.getByRole('button', { name: '+ Add SOW / Contract' }).click();
          await expect(page.getByRole('dialog', { name: 'Add SOW / Contract' })).toBeVisible({
            timeout: 15_000,
          });
        },
      });
    }

    // Derived from the real screens -- see SCREEN_SCAN_ALLOWANCE_MS and INTERACTION_ALLOWANCE_MS.
    test.setTimeout(budgetFor(screens));

    // Still a floor rather than an equality, because the three id-dependent screens are skipped when
    // the seeded directory carries no assignment. The two unconditional ones are the real assertion.
    expect(await sweep(page, screens), 'assignment write surfaces scanned').toBeGreaterThanOrEqual(
      2,
    );
  });

  /**
   * Administrative configuration — named explicitly by FR-085, and entirely unscanned before this.
   */
  test('administrative configuration has no WCAG 2.1 AA violations at any viewport', async ({
    page,
  }) => {
    const screens: Screen[] = [
      { name: 'Admin index', path: '/compass/admin', ready: heading('Compass Administration') },
      {
        name: 'Lookup administration',
        path: '/compass/admin/lookups',
        ready: heading('Lookup Administration'),
      },
      { name: 'EDJEr list', path: '/compass/admin/edjers', ready: heading('EDJErs') },
      { name: 'EDJEr add form', path: '/compass/admin/edjers/new', ready: heading('Profile') },
      {
        name: 'EDJEr edit form',
        path: `/compass/admin/edjers/${employeeId}`,
        ready: heading('Profile'),
      },
      { name: 'Client list', path: '/compass/admin/clients', ready: heading('Clients') },
      {
        name: 'Client add form',
        path: '/compass/admin/clients/new',
        ready: heading('Client Details'),
      },
      {
        name: 'Client edit form',
        path: `/compass/admin/clients/${clientId}`,
        ready: heading('Client Details'),
      },
    ];

    // Derived from the real screens -- see SCREEN_SCAN_ALLOWANCE_MS and INTERACTION_ALLOWANCE_MS.
    test.setTimeout(budgetFor(screens));

    expect(await sweep(page, screens), 'administrative screens scanned').toBe(8);
  });

  /**
   * The not-found page, which is a real destination rather than an error state: `CompassNav` links to
   * areas that may not be routed yet, and a person who lands here still has to be able to read it.
   */
  test('the not-found page has no WCAG 2.1 AA violations at any viewport', async ({ page }) => {
    const scanned = await sweep(page, [
      {
        name: 'Not found',
        path: '/compass/no-such-screen',
        ready: heading('Page Not Found'),
      },
    ]);

    expect(scanned, 'not-found scanned').toBe(1);
  });

  /**
   * Navigates to each screen, waits for it to settle, scans it at all five viewports, and returns how
   * many it scanned.
   *
   * **The count is returned so each caller can assert it.** A sweep driven by a list is exactly the
   * shape that passes by covering nothing — this repository found eight gates silently failing open
   * that way. An empty or truncated list would otherwise report success.
   */
  async function sweep(page: Page, screens: Screen[]): Promise<number> {
    for (const screen of screens) {
      // A 1280x800 baseline before each navigation, so a screen never inherits the 390px viewport the
      // previous one finished on and gets scanned in a layout its `ready` locator was not written for.
      await page.setViewportSize({ width: 1280, height: 800 });
      await page.goto(screen.path, { timeout: 15_000 });
      await screen.ready(page);
      if (screen.open) {
        await screen.open(page);
      }

      await expectNoWcagViolations(page, screen.name);
    }

    return screens.length;
  }
});
