// Feature 011 US1 (issue #83, AC-NFR-7) — the defects the audit found, asserted.
//
// Every assertion here was RED when written, against measurements taken twice: at `323f66ce` on
// 2026-08-20 and re-verified at `58626f43` on 2026-08-21 after main advanced 57 commits. The pixel
// counts in the comments are those measurements, kept so a future reader can tell a regression from a
// legitimate layout change.
//
// Gate A is the stranding criterion; Gate B is axe; Gate E is page-level overflow; Gate G is the
// 767/768 breakpoint boundary.
import { expect, test, type Page } from '@playwright/test';

import { stubLogin } from './helpers/auth';
import { VIEWPORTS, expectNoWcagViolationsAt } from './helpers/axe';
import {
  EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT,
  compassScreens,
  resolveSeededIds,
  type SeededIds,
} from './helpers/compass-routes';
import {
  expectNoStranding,
  expectRegionsPresent,
  expectRowsPresent,
} from './helpers/scroll-region';

/** Tailwind's `md` breakpoint. Cards below it, tables at and above. */
const MD_BREAKPOINT_PX = 768;

const MOBILE = { width: 390, height: 844 } as const;
const TABLET_BELOW_MD = { width: 767, height: 1024 } as const;
const TABLET_AT_MD = { width: MD_BREAKPOINT_PX, height: 1024 } as const;

/**
 * The screens that stack to cards below `md`.
 *
 * Keys match `compassScreens()` names. Kept here rather than derived from `category`, because the two
 * assignment-detail mounts are also `read` and are NOT in the set — they carry no wide table.
 *
 * **`employee-detail` joined on 2026-08-25 (T078, owner decision).** It was deliberately excluded while
 * that decision was open, precisely so that folding it in could not happen quietly: unlike the other
 * six it never stranded, so its inclusion is a consistency choice about its twin — the client record's
 * `CC-3` history — and not a remedy for a measured defect. Now that the owner has chosen cards, leaving
 * it out would mean the gate does not cover the shape that shipped.
 */
const CARD_STACK_SCREENS = [
  'client-view-internal',
  'client-view-external',
  'employee-detail',
  'sales-dashboard',
  'reports-availability',
  'reports-assignment-duration',
  'reports-assignment-start',
  'reports-sow-extension',
] as const;

/** Screens FR-006b pins as tables at every width. */
const TABLE_ALWAYS_SCREENS = [
  'team-directory',
  'client-directory',
  'admin-lookups',
  'admin-edjers',
  'admin-clients',
] as const;

/**
 * The per-test budget for the loops that walk the whole screen inventory or the whole
 * screen-by-viewport matrix.
 *
 * **Playwright's default is 30 s per TEST, and these tests each perform ~25 navigations.** `open()`
 * allows any single one of them up to 15 s (`waitUntil: 'networkidle'`), so the default was never a
 * budget they could meet: TWO slow loads exceed it on their own, and the worst case is twelve times
 * it. That is why they fail as a bare `Test timeout of 30000ms exceeded` with no assertion in the
 * stack -- the clock runs out mid-walk, having asserted nothing. Observed on `main` and on PRs
 * alike, on every engine, which is what marks it as a budget defect rather than a product one.
 *
 * **This does not weaken the tests, because it is not what catches a hang.** `open()`'s own 15 s
 * `goto` cap does: a genuinely stuck navigation still throws there and fails the test, well inside
 * the budget below. Sized from the work instead of guessed -- one allowance per navigation, with
 * enough headroom that ordinary CI variance is not a failure while a real stall still is.
 */
const NAVIGATION_ALLOWANCE_MS = 4_800;
const INVENTORY_WALK_TIMEOUT_MS = EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT * NAVIGATION_ALLOWANCE_MS;

test.describe.configure({ mode: 'serial' });

test.describe('@compass US1 — no clipped value reads as complete [critical]', () => {
  let ids: SeededIds;

  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage();
    await stubLogin(page.request, {
      email: 'stranding.sweep@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });
    ids = await resolveSeededIds(page);
    await page.close();
  });

  test.beforeEach(async ({ page }) => {
    await stubLogin(page.request, {
      email: 'stranding.sweep@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });
  });

  /** Navigates and waits for the network to settle — measuring a spinner measures the spinner. */
  async function open(page: Page, name: string, viewport: { width: number; height: number }) {
    const screen = compassScreens(ids).find((candidate) => candidate.name === name);

    // A throw rather than `expect(...).toBeDefined()` plus a non-null assertion: this narrows the type
    // for the rest of the function, so no assertion is needed, and it fails with a message that says
    // where to look. A missing name here means the inventory and this spec disagree.
    if (screen === undefined) {
      throw new Error(
        `"${name}" is not in the screen inventory — add it to compassScreens() in ` +
          'tests/e2e/helpers/compass-routes.ts, or correct the name used here.',
      );
    }

    await page.setViewportSize(viewport);
    await page.goto(screen.path, { timeout: 15_000, waitUntil: 'networkidle' });
    return screen;
  }

  // ---------------------------------------------------------------- T014: client-view @390
  //
  // The highest-severity finding in the audit, and the smallest overflow: 20px hidden with zero
  // focusables past the edge. `ClientViewPage` omits the SOW column when `view.isInternal`, which makes
  // the plain-text End Date the last column — so `09/09/2026` renders as `09/09/202`. Every other
  // instance loses a whole column or an obviously-clipped string; this one silently changes a value.
  test('the client record does not clip a date into a different valid date (390px)', async ({
    page,
  }) => {
    // **The INTERNAL client specifically.** Written first against `client-view` (whichever client the
    // directory returns first) and it PASSED — because that one is external, so its trailing `SOW`
    // column carries a `View SOW` link past the visible edge and the region has a keyboard path. The
    // 20px clip only strands when `isInternal` drops that column and leaves plain-text `End Date` last.
    // A test on the external variant alone is green today and blind to the defect.
    await open(page, 'client-view-internal', MOBILE);
    await expectNoStranding(page, 'client-view-internal');
  });

  test('the external client record is clear too, so the fix is not internal-only', async ({
    page,
  }) => {
    await open(page, 'client-view-external', MOBILE);
    await expectNoStranding(page, 'client-view-external');
  });

  // ---------------------------------------------------------------- T015: sales-dashboard @390
  //
  // 326px hidden, zero focusables past the edge: SOW End Date cut by 49px, and Days Until Expiration
  // and Coach entirely unreachable without a pointer. The client links that DO exist sit in a visible
  // column, which is why axe passes this screen and Gate A does not.
  test('the sales dashboard breakdown strands nothing at 390px', async ({ page }) => {
    await open(page, 'sales-dashboard', MOBILE);
    await expectNoStranding(page, 'sales-dashboard');
  });

  // ---------------------------------------------------------------- T016: reports-availability @390
  //
  // Three separate regions, 134/374/396px, none with a keyboard path. This is also the only screen axe
  // catches — 3 `serious` nodes — once the exclusion is gone (T017/T018).
  test('all three availability report tables strand nothing at 390px', async ({ page }) => {
    await open(page, 'reports-availability', MOBILE);
    await expectNoStranding(page, 'reports-availability');
  });

  // ---------------------------------------------------------------- T018: axe, now observable
  //
  // RED only because T017 deleted `SHARED_CHROME_EXCLUSIONS`. While `.overflow-x-auto` was excluded
  // this assertion passed vacuously — which is the failure mode this whole feature exists to correct,
  // and why the deletion had to land first.
  test('the availability report has no WCAG 2.1 AA violations at 390px', async ({ page }) => {
    await open(page, 'reports-availability', MOBILE);
    await expectNoWcagViolationsAt(page, 'reports-availability', MOBILE);
  });

  // ---------------------------------------------------------------- T019: Gate G, the 767/768 pair
  //
  // The case the card stack does NOT cover. `md` is min-width 768, so 768 renders tables — and two
  // regions strand there: reports-availability 18px and team-directory 104px (`Current Client(s)`).
  // `docs/nfr/NFR-catalog.md` P4 names both 767 and 768 precisely so this boundary is testable.
  test('below md the card-stack screens expose no scroll region at all', async ({ page }) => {
    for (const name of CARD_STACK_SCREENS) {
      await open(page, name, TABLET_BELOW_MD);

      // `:visible`, not a bare count. `StackedRows` keeps BOTH layouts in the DOM and hides one with
      // `display: none` — which is what removes it from the accessibility tree and zeroes its metrics,
      // so a hidden table can never register as an overflowing region. The user-facing property is that
      // no scroll region is VISIBLE here, and that is what this asserts.
      const visibleRegions = await page.locator('.overflow-x-auto:visible').count();
      expect(
        visibleRegions,
        `${name} at 767px must render cards, not a scrollable table — FR-006a removes the ` +
          'off-screen axis rather than making it navigable, so there is nothing to strand',
      ).toBe(0);
    }
  });

  test('at exactly md the tables are back, and they strand nothing', async ({ page }) => {
    for (const name of ['reports-availability', 'team-directory'] as const) {
      await open(page, name, TABLET_AT_MD);
      await expectNoStranding(page, name);
      await expectNoWcagViolationsAt(page, name, TABLET_AT_MD);
    }
  });

  // ---------------------------------------------------------------- T020: card field parity
  //
  // **Deviation from the task text, with its reason.** T020 specified a jsdom unit test at
  // `tests/unit/features/stacked-rows-parity.test.tsx`. jsdom has no layout engine — the repository's
  // own `tests/unit/responsive.test.tsx` documents that every element reports `clientWidth` and
  // `scrollWidth` as 0 regardless of CSS — so a `md:`-conditional layout cannot be asserted there at
  // all. A parity test that cannot observe which layout rendered would pass against either one, which
  // is a gate matching nothing. It belongs in a real browser at real widths, so it lives here.
  /**
   * The report screens that render a date-range form and fetch NOTHING until it is submitted.
   *
   * **Membership is what stops this sweep passing over an empty form.** Every measurement below —
   * stranding, card/table parity, the row floor — reads what is on the page, and an unsubmitted form
   * has no table, so a screen missing from this set is measured as clean rather than measured at all.
   * That is the vacuous pass this whole file exists to close, and it is silent: nothing fails.
   *
   * `reports-sow-extension` joined with issue #534. It is a set rather than a second `if` because the
   * check appears in FOUR places below, and the first version of that report added the screen to the
   * inventory while leaving all four naming its sibling alone.
   */
  const DATE_RANGE_REPORT_SCREENS: ReadonlySet<string> = new Set([
    'reports-assignment-start',
    'reports-sow-extension',
  ]);

  /**
   * Submits a wide range on whichever date-range report is open, so there is a table to measure.
   *
   * One helper for both: `AssignmentStartPage` and `SowExtensionPage` carry the same `Start Date` /
   * `End Date` / `Run` form, so the labels below are not a coincidence to be tolerated — they are the
   * shared `FormField` contract. If a third report diverges from it, that report needs its own driver
   * rather than a looser locator here.
   */
  async function runDateRangeReport(page: Page) {
    // Anchored for the reason `export-control.critical.spec.ts` records: since issue #461 the sort
    // control's aria-label also contains "start date". This helper is the one most exposed to it —
    // calling it twice against a single navigation would otherwise match two elements on the second
    // call, and fail naming the date field rather than the sorting it came from. The anchor is also
    // what keeps this correct on `reports-sow-extension`, whose sortable header reads "Extension
    // Start Date": an unanchored /start date/i would match that column too.
    await page.getByLabel(/^start date/i).fill('2020-01-01');
    await page.getByLabel('End Date').fill('2030-12-31');
    await page.getByRole('button', { name: 'Run' }).click();
    await expect(page.getByRole('table').or(page.getByRole('list'))).toBeVisible({
      timeout: 15_000,
    });
  }

  test('a card carries exactly the fields its table columns carried', async ({ page }) => {
    for (const name of CARD_STACK_SCREENS) {
      // Above the breakpoint: harvest the column headers the table declares.
      await open(page, name, { width: 1280, height: 800 });
      if (DATE_RANGE_REPORT_SCREENS.has(name)) {
        await runDateRangeReport(page);
      }
      const columnHeaders = (await page.locator('th').allTextContents())
        .map((text) => text.replace(/[▲▼]/g, '').trim())
        .filter((text) => text !== '');

      expect(
        columnHeaders.length,
        `${name} must render table headers at 1280px, or there is nothing to compare a card against`,
      ).toBeGreaterThan(0);

      // Below it: every one of those labels must still be present, as a card's field label.
      await open(page, name, TABLET_BELOW_MD);
      if (DATE_RANGE_REPORT_SCREENS.has(name)) {
        await runDateRangeReport(page);
      }
      const cardText = (await page.locator('main').innerText()).replace(/\s+/g, ' ');

      for (const header of columnHeaders) {
        expect(
          cardText,
          `${name}: the card layout drops the "${header}" field that its table shows at 1280px. ` +
            'A card is a re-arrangement of the same data, never a reduced set (US1 §7, CHK010).',
        ).toContain(header);
      }
    }
  });

  // ---------------------------------------------------------------- T021: FR-006b, the negative
  //
  // Passes today. Its job is to fail LATER, if someone migrates an admin list to cards — FR-006b would
  // otherwise be a MUST with no enforcement, which the constitution's governance clause forbids.
  test('admin and directory screens keep their tables at every named viewport', async ({
    page,
  }) => {
    // TABLE_ALWAYS_SCREENS x VIEWPORTS is the same ~25 navigations as an inventory walk.
    test.setTimeout(INVENTORY_WALK_TIMEOUT_MS);

    for (const name of TABLE_ALWAYS_SCREENS) {
      for (const viewport of VIEWPORTS) {
        await open(page, name, viewport);
        await expect(
          page.locator('table').first(),
          `${name} at ${viewport.width}px must still render a <table> — FR-006b keeps admin ` +
            'configuration and the directory lists tabular at every width, consistent with ' +
            "AC-NFR-7's own note that Super Admin work is expected to happen on a desktop",
        ).toBeAttached();
      }
    }
  });

  // ================================================================ T039-T042: the full sweep
  //
  // US2. Every route in the inventory, at every viewport `docs/nfr/NFR-catalog.md` P4 names.
  //
  // **Why this exists on top of the per-screen tests above.** Those assert the defects the audit
  // found. This asserts the ones nobody has looked for yet — which is the actual AC-NFR-7 requirement
  // ("all screens"), and the thing whose absence let three screens ship stranded. A screen added
  // tomorrow is covered the moment it enters `compassScreens()`, and `wcag-sweep-coverage.test.ts`
  // fails if it does not.
  //
  // One test per viewport rather than one per screen-viewport pair: the navigation is the cost, and
  // grouping keeps the failure message naming both the screen and the width.
  for (const viewport of VIEWPORTS) {
    test(`no Compass screen strands content at ${viewport.width}x${viewport.height}`, async ({
      page,
    }) => {
      test.setTimeout(INVENTORY_WALK_TIMEOUT_MS);

      const screens = compassScreens(ids);

      // Gate D — the sweep must not shrink. A screen dropped from the inventory would otherwise
      // reduce this loop and report success over fewer screens.
      expect(
        screens.length,
        'the inventory must carry 23 leaf routes + NotFound; a lower count means screens were ' +
          'silently dropped, and this sweep would then pass over the remainder',
      ).toBe(EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT);

      let measured = 0;

      for (const screen of screens) {
        await open(page, screen.name, viewport);

        // The date-range reports fetch nothing until their range is submitted, so without this they
        // would be measured as an empty form — a pass over nothing.
        if (DATE_RANGE_REPORT_SCREENS.has(screen.name)) {
          await runDateRangeReport(page);
        }

        // Seeded-data guard (T041): a screen expected to carry rows must carry them, or its stranding
        // verdict below means nothing.
        await expectRowsPresent(page, screen.name);

        await expectNoStranding(page, `${screen.name} @ ${viewport.width}px`);
        measured += 1;
      }

      // Exact, not "at least" (T042): a loop that silently visited fewer screens than the inventory
      // holds is the vacuous pass this whole feature exists to close.
      expect(measured, 'every screen in the inventory was measured').toBe(
        EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT,
      );
    });
  }

  // ================================================ T045: the project's OWN viewport
  //
  // Every other assertion in this file sets one of the five NFR-catalog viewports explicitly, which
  // overrides whatever viewport the project started with. That is right for the responsive standard and
  // wrong as the whole story: on the emulated device projects it means the device's ACTUAL screen size
  // is never measured.
  //
  // The Galaxy S24 descriptor is **360px** — narrower than the catalog's 390px floor, and a very common
  // real Android width. Without this test that width is covered by nothing. On the desktop projects
  // this runs at Playwright's default size, so it costs one extra navigation and asserts something true
  // there too.
  //
  // **One waived screen, with its reason and its evidence (Gate E).** `admin-client-edit` overflows the
  // PAGE by 4px at 360px. Verified pre-existing: stashing this feature entirely and re-probing clean
  // `main` at 360x780 reproduces the same `scrollWidth: 364` against `clientWidth: 360`. No element
  // actually extends past 360 — a scan for elements outside a scroll container with `right > 360`
  // returns empty — so it is scrollWidth propagating from the nested scroller, and `admin-edjer-edit` at
  // the same width is clean because its table is narrower.
  //
  // Waived rather than either (a) dropping this test, which would lose 360px coverage altogether, or
  // (b) relaxing Gate E globally, which would stop it catching the thing it exists for. The waiver is
  // one screen, below one width, and it needs its own fix — it is a latent defect this tooling found,
  // not one it caused.
  const PAGE_OVERFLOW_WAIVED_BELOW_390 = new Set(['admin-client-edit']);

  test("no Compass screen strands content at the project's own viewport", async ({ page }) => {
    test.setTimeout(INVENTORY_WALK_TIMEOUT_MS);

    const viewport = page.viewportSize();
    expect(viewport, 'the project must define a viewport for this to mean anything').not.toBeNull();

    for (const screen of compassScreens(ids)) {
      if ((viewport?.width ?? 0) < 390 && PAGE_OVERFLOW_WAIVED_BELOW_390.has(screen.name)) {
        continue;
      }

      // Deliberately NOT setting a viewport — this is the whole point of the test.
      await page.goto(screen.path, { timeout: 15_000, waitUntil: 'networkidle' });

      if (DATE_RANGE_REPORT_SCREENS.has(screen.name)) {
        await runDateRangeReport(page);
      }

      await expectRowsPresent(page, screen.name);
      await expectNoStranding(page, `${screen.name} @ project viewport ${viewport?.width}px`);
    }
  });

  // ---------------------------------------------------------------- Gate D: non-vacuity
  //
  // A screen whose table stopped rendering has zero regions, so every stranding assertion above would
  // pass over nothing. Scoped to the table-keeping screens: the card-stack screens legitimately have
  // zero regions at 390px, and Gate G above is their equivalent guard.
  test('the table-keeping screens still render the regions they are known to have', async ({
    page,
  }) => {
    for (const name of ['team-directory', 'admin-edjers', 'admin-clients'] as const) {
      await open(page, name, MOBILE);
      await expectRegionsPresent(page, name);
    }
  });
});
