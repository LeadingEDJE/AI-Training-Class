// The Sales Dashboard (AC-36, AC-37, J18 + variant 18a) against real seeded data, plus the
// server-side denial and the axe-core sweep at the five AC-NFR-7 viewports.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';

import { expectNoWcagViolations } from './helpers/axe';

async function signInAsCompassSales(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=sales.dashboard@example.test&groups=Compass-Sales-dev',
    { maxRedirects: 0 },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

test.describe.configure({ mode: 'serial' });

/**
 * **This file asserts the DESKTOP table presentation, so it is pinned to a desktop width (issue #83,
 * T045).**
 *
 * Its subject is the mockup's table structure — column counts and order, sort order, cell formatting —
 * and `docs/design/edje-compass-mockups.html` describes presentation at >= 900px only. Below `md` this
 * screen renders label-value cards instead (FR-006a), so there is no `thead` to count and these claims
 * do not apply to what is on screen.
 *
 * That desktop-ness was already true and merely IMPLICIT: every one of these tests relied on
 * Playwright's 1280x720 project default, which is why they passed for months and then failed the moment
 * the emulated-device projects ran them at 390px. Making it explicit is the fix; the alternative —
 * rewriting every locator to be layout-agnostic — is a much larger change that would assert the same
 * data twice.
 *
 * **What still covers this screen on a phone**, so the pin is not a coverage loss:
 * - stranding and page-overflow at all five NFR viewports AND at the device's own viewport
 *   (`responsive-stranding.critical.spec.ts`)
 * - card field parity against the desktop columns, asserted at 767 against 1280 (same file)
 * - axe at all five viewports — the sweep below sets its own widths per iteration, so this pin does
 *   not touch it
 */
test.use({ viewport: { width: 1280, height: 800 } });

test.describe('Sales Dashboard [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsCompassSales(page);
  });

  test('renders all four tiles with counts against one as-of date (AC-36)', async ({ page }) => {
    await page.goto('/compass/sales-dashboard');

    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });
    // Issue #343: no "as of" date banner under the title — it's inherent that the counts are as of
    // whenever the page is loaded, so the underlying business-date computation stays but the text
    // announcing it does not.
    await expect(page.getByText(/as of/i)).not.toBeVisible();

    // The mockup's four qualifying labels, verbatim (FR-028). Tiles are real <button>s styled to
    // the mockup's whole-tile click target, not headings — matching the mockup's own markup, which
    // has no heading element on a tile either. A substring match is safe here: unlike the tile's own
    // accessible name (which the count number precedes), the breakdown panel's Card heading uses the
    // SHORTER `detailTitle` form and never collides with these full captions.
    for (const label of [
      'Active Client Assignments',
      'SOWs Expiring < 90 Days (no follow-on SOW)',
      'Confirmed Rollouts (all assignments have an end date)',
      'EDJErs on the Beach',
    ]) {
      await expect(page.getByRole('button', { name: label })).toBeVisible();
    }
  });

  test('opens on the expiring-SOWs breakdown, and swaps rather than stacking when another tile is chosen (J18, variant 18a)', async ({
    page,
  }) => {
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });

    // FR-028: opens on expiring SOWs, not an empty panel. The breakdown Card's title is the
    // mockup's own shorter form (its <h2>SOWs Expiring &lt; 90 Days</h2>, no "(no follow-on SOW)"
    // repeated from the tile caption) — exact: true, since the tile's own accessible name ("7 SOWs
    // Expiring < 90 Days (no follow-on SOW)") would otherwise substring-match too.
    const initialTable = page.getByRole('table');
    await expect(initialTable).toBeVisible({ timeout: 15_000 });
    await expect(
      page.getByRole('heading', { name: 'SOWs Expiring < 90 Days', exact: true }),
    ).toBeVisible();

    // AC-37 / FR-007: selecting another tile replaces the breakdown — never two at once.
    // Tiles are the whole click target (mockup `.tile{cursor:pointer}`), not a nested button.
    await page.getByRole('button', { name: /Beach/i }).click();

    await expect(
      page.getByRole('heading', { name: 'EDJErs on the Beach', exact: true }),
    ).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('table')).toHaveCount(1);
  });

  // T101 / CHK008 + CHK018 — the SD-2 breakdown is a genuine AC-superset: AC-37 names four fields,
  // the mockup shows FIVE, and "Days Until Expiration" is the one an AC-only build drops. Column
  // ORDER is asserted, and the "#" prefix is asserted ABSENT here — it is PRESENT on the Availability
  // report's day-count headers (reports-availability.critical.spec.ts), and the mockup is deliberately
  // inconsistent between the two, which FR-026 rule 3 says to copy rather than normalise.
  //
  // Issue #332 makes it SIX: "Type" sits next to the EDJEr's name on this grid and on the
  // active-client-assignments and confirmed-rollouts grids, matching the Team Directory's own pill.
  // The beach grid is the exception (a 1099 EDJEr is never on the beach) and is asserted separately below.
  test('the expiring-SOWs breakdown shows its mockup columns in order, no "#" prefix (SD-2, issue #332)', async ({
    page,
  }) => {
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });
    const table = page.getByRole('table');
    await expect(table).toBeVisible({ timeout: 15_000 });

    // `toContainText`, not `toHaveText`: since issue #462 these headers are sort controls and the
    // ordered one carries a trailing caret glyph, so an exact-text assertion would fail on the caret
    // rather than on anything this test is about. Order and content are still both asserted — the
    // beach grid's own header assertion above made the same accommodation for issue #464.
    await expect(table.getByRole('columnheader')).toContainText([
      'EDJEr',
      'Type',
      'Client',
      'SOW End Date',
      'Days Until Expiration',
      'Coach',
    ]);
    await expect(
      table.getByRole('columnheader', { name: /^#/ }),
      'the dashboard breakdown must NOT prefix its day-count header with "#" (it does on Availability)',
    ).toHaveCount(0);
  });

  // Issue #459/#633: the Active Client Assignments breakdown shows six columns (the assignment's own
  // start date alongside the MAX SOW end date across its SOWs) and every header is a click-to-sort
  // control, client-side.
  test('the active-client-assignments breakdown shows its six columns and re-orders on a header click (issue #459, #633)', async ({
    page,
  }) => {
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });

    await page.getByRole('button', { name: /Active Client Assignments/ }).click();
    await expect(
      page.getByRole('heading', { name: 'Active Client Assignments', exact: true }),
    ).toBeVisible({
      timeout: 15_000,
    });

    const table = page.getByRole('table');
    await expect(table).toBeVisible({ timeout: 15_000 });
    // `toContainText`, not `toHaveText`: since issue #459 these headers are sort controls and the
    // ordered column's header carries a caret glyph beside its label — the expiring-SOWs and beach
    // assertions above already use `toContainText` for the same reason.
    await expect(table.getByRole('columnheader')).toContainText([
      'EDJEr',
      'Type',
      'Client',
      'Assignment Start',
      'SOW End Date',
      'Coach',
    ]);

    // Opens on EDJEr name ascending — the server's primary order (EmployeeName), which DEFAULT_SORT
    // reproduces so selecting the tile shows the rows in the order the API returned them.
    const rows = table.locator('tbody tr');
    await expect(rows.first()).toBeVisible({ timeout: 15_000 });
    await expect(table.locator('thead th').first()).toHaveAttribute('aria-sort', 'ascending');
    const before = await rows.locator('td:nth-child(1)').allInnerTexts();
    expect(before.length).toBeGreaterThan(1);
    expect(before).toEqual([...before].sort((a, b) => a.localeCompare(b)));

    // EDJEr is already the ordered column, so clicking it REVERSES the order (no re-fetch) — the same
    // rows, now descending, with aria-sort flipped.
    await page.getByRole('button', { name: 'Sort by edjer' }).click();
    const after = await rows.locator('td:nth-child(1)').allInnerTexts();
    expect([...after].sort()).toEqual([...before].sort());
    expect(after).toEqual([...before].reverse());
    await expect(table.locator('thead th').first()).toHaveAttribute('aria-sort', 'descending');
  });

  // Issue #331: every beach row is an assignment to an internal EDJE client, so naming it adds no
  // information — the Availability Report's equivalent "Currently Available EDJErs" section already
  // omits it (AC-38) for the same reason.
  test('the beach breakdown has no Client column, unlike the other three categories (issue #331)', async ({
    page,
  }) => {
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });

    await page.getByRole('button', { name: /Beach/i }).click();
    await expect(
      page.getByRole('heading', { name: 'EDJErs on the Beach', exact: true }),
    ).toBeVisible({ timeout: 15_000 });

    const table = page.getByRole('table');
    await expect(table).toBeVisible({ timeout: 15_000 });
    await expect(table.getByRole('columnheader')).toContainText([
      'EDJEr',
      'Assignment Start Date',
      'Days Available',
      'Coach',
    ]);
  });

  // Issue #464: click-to-sort headers on the beach breakdown, matching the Client Assignment
  // Duration report's own sort affordance.
  test.describe('beach breakdown sorting (issue #464)', () => {
    /** The rendered Assignment Start Date column, top to bottom. */
    async function startDateColumn(page: Page): Promise<string[]> {
      const rows = page.getByRole('table').locator('tbody tr');
      await expect(rows.first()).toBeVisible({ timeout: 15_000 });
      return rows.locator('td:nth-child(2)').allInnerTexts();
    }

    test.beforeEach(async ({ page }) => {
      await page.goto('/compass/sales-dashboard');
      await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
        timeout: 15_000,
      });
      await page.getByRole('button', { name: /Beach/i }).click();
      await expect(
        page.getByRole('heading', { name: 'EDJErs on the Beach', exact: true }),
      ).toBeVisible({ timeout: 15_000 });
    });

    test('orders by Assignment Start Date, oldest first, by default and marks it with aria-sort', async ({
      page,
    }) => {
      const dates = (await startDateColumn(page)).map((cell) => new Date(cell));
      expect(dates.length).toBeGreaterThan(0);

      const ascending = [...dates].sort((a, b) => a.getTime() - b.getTime());
      expect(
        dates,
        'the beach breakdown must open ordered by Assignment Start Date, oldest first',
      ).toEqual(ascending);

      const table = page.getByRole('table');
      await expect(table.locator('thead th').nth(1)).toHaveAttribute('aria-sort', 'ascending');
    });

    // ONE click, not two: this grid opens ordered by Assignment Start Date, so the first click on
    // that header is already the reversal (`INITIAL_DIRECTION` is consulted only on arrival from a
    // different column). The title said "on a second click" until issue #462 corrected it — the same
    // wording bug the #461 review found in the assignment-start suite.
    test('re-sorts a column without changing the data set, and reverses it fully', async ({
      page,
    }) => {
      const before = await startDateColumn(page);

      await page.getByRole('button', { name: 'Sort by assignment start date' }).click();
      const after = await startDateColumn(page);

      // Same rows, reversed — no re-fetch, matching the Client Assignment Duration report's FR-014.
      expect(after).toHaveLength(before.length);
      expect([...after].sort()).toEqual([...before].sort());
      expect(after).toEqual([...before].reverse());

      const table = page.getByRole('table');
      await expect(table.locator('thead th').nth(1)).toHaveAttribute('aria-sort', 'descending');
    });

    test('every OTHER beach header is also a working sort control', async ({ page }) => {
      for (const label of ['EDJEr', 'Days Available', 'Coach']) {
        const sortButton = page.getByRole('button', { name: `Sort by ${label.toLowerCase()}` });
        await sortButton.click();

        // The header is located by the BUTTON IT CONTAINS, not by its own accessible name.
        //
        // `SortButton` carries an explicit `aria-label` ("Sort by edjer"), which REPLACES its text
        // content when the browser computes a name -- and a `th`'s name comes from its contents. So
        // in Chrome this header is named "Sort by edjer", not "EDJEr", and
        // `getByRole('columnheader', { name: /EDJEr/ })` matches nothing. jsdom computes it from the
        // text instead, which is why the unit suite is happy with the same query and this one was
        // not: the two engines disagree, and only the real browser's answer counts here.
        //
        // The lowercase label is deliberate and is covered by ui.test.tsx's WCAG 2.5.3 case, so the
        // component is right and the locator was wrong. Containment sidesteps the disagreement
        // entirely, and stays readable in a way `thead th` index arithmetic does not.
        const header = page.locator('thead th').filter({ has: sortButton });

        await expect(header, `${label} must report its sort state on the header`).toHaveAttribute(
          'aria-sort',
          /ascending|descending/,
        );
      }
    });
  });

  // Issue #462, on the grid the page opens on (FR-028) — so no tile click is needed to reach it.
  //
  // ONE test, for WIRING only, and it deliberately asserts
  // NO ordering. What is browser-unique here is that the control reaches the real DOM, that the
  // header carries `aria-sort` where assistive technology looks for it, and that re-ordering asks
  // the server nothing. Every ordering rule — each column, both directions, the null rule, the
  // multi-client key — is Vitest's in `SalesDashboardPage.test.tsx`, and TEST-STRATEGY ruling 3 puts
  // a render assertion there rather than here. An earlier draft of this test did assert the A-Z
  // order, justified by the browser and Node disagreeing about collation; computing the expected
  // order inside the page removed that disagreement and with it the justification, so the assertion
  // went back to the layer that owns it.
  //
  // **Issue #463's grid is not exercised here and does not need to be**: the two grids are the same
  // code path (one `COLUMNS` entry each, one comparator), differing only in two header strings, and
  // the unit suite runs every case against both.
  test('the expiring-SOWs headers are live sort controls that ask the server nothing (issue #462)', async ({
    page,
  }) => {
    // Watch the wire from BEFORE the navigation, so the baseline below is the breakdown's own initial
    // lookup rather than zero. Attaching the listener after the page has loaded — as this first did —
    // makes the "before" count 0 and turns the subtraction into an absolute count, which is exactly
    // the assertion the comment claimed not to be making.
    const breakdownRequests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/compass/dashboard/breakdown')) {
        breakdownRequests.push(request.url());
      }
    });

    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });
    const table = page.getByRole('table');
    await expect(table).toBeVisible({ timeout: 15_000 });

    const edjers = table.locator('tbody tr td:nth-child(1)');
    await expect(edjers.first()).toBeVisible({ timeout: 15_000 });
    const before = await edjers.allInnerTexts();
    expect(before.length, 'the seeded expiring-SOWs breakdown must have rows').toBeGreaterThan(0);

    // Non-zero, which is what proves the listener is actually attached and matching: the rows on
    // screen were fetched, so at least one breakdown request must have been seen. A baseline that
    // came back 0 here would mean the URL filter had drifted, and the click assertion below would
    // then pass forever.
    const requestsBeforeSorting = breakdownRequests.length;
    expect(
      requestsBeforeSorting,
      'the initial breakdown lookup must have been observed, or the request filter is stale',
    ).toBeGreaterThan(0);

    const sortButton = page.getByRole('button', { name: 'Sort by edjer' });
    await sortButton.click();

    // The header is located by the BUTTON IT CONTAINS — `SortButton`'s explicit `aria-label`
    // REPLACES its text when a browser computes the `th`'s name, so `getByRole('columnheader',
    // { name: /EDJEr/ })` matches nothing in Chrome even though jsdom is happy with it. The comment
    // on the beach block above has the full reasoning.
    await expect(page.locator('thead th').filter({ has: sortButton })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );

    // The same rows, re-ordered client-side over the list already fetched (FR-014) — and no new
    // request caused BY THE CLICK. The count is baseline-relative because `main.tsx` builds a bare
    // `QueryClient`: `refetchOnWindowFocus` is on and `staleTime` is 0, so an absolute count would
    // let an unrelated refetch read as the feature re-querying.
    const after = await edjers.allInnerTexts();
    expect(after).toHaveLength(before.length);
    expect([...after].sort()).toEqual([...before].sort());
    expect(
      breakdownRequests.length - requestsBeforeSorting,
      'a header click must re-order rows already on the page, not ask the server again',
    ).toBe(0);
  });

  // Issue #330: the EDJEr and the Coach are links too, not only the Client. One test over all three,
  // because the claim is about the grid — every name in it drills into that record.
  //
  // NOTE the client half is located BY HREF, not by position. It used to be
  // `getByRole('link').first()`, which meant "the client link" only for as long as the client was
  // the sole link in the row; the EDJEr column comes first, so `.first()` now finds the employee.
  test('every name in a breakdown row is a real link to that record (issue #330)', async ({
    page,
  }) => {
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });
    const table = page.getByRole('table');
    await expect(table).toBeVisible({ timeout: 15_000 });

    // No stray links: everything in the grid points at one of the two record screens.
    const hrefs = await table
      .getByRole('link')
      .evaluateAll((links) => links.map((link) => link.getAttribute('href') ?? ''));
    expect(hrefs.length, 'the breakdown must render some links at all').toBeGreaterThan(0);
    for (const href of hrefs) {
      expect(href).toMatch(/^\/compass\/(team-directory|client-directory)\/\d+$/);
    }

    // The EDJEr's name, on EVERY row — one employee link per row at minimum, so a grid that linked
    // only the first row would fail here rather than pass on a single sample.
    const rowCount = await table.locator('tbody tr').count();
    expect(rowCount, 'the seeded expiring-SOWs breakdown must have rows').toBeGreaterThan(0);
    const employeeHrefs = hrefs.filter((href) => href.startsWith('/compass/team-directory/'));
    expect(employeeHrefs.length).toBeGreaterThanOrEqual(rowCount);

    // The coach, where there is one. Asserting the non-empty count FIRST, so seeded data without a
    // single coach fails this test instead of skipping its point — a loop over zero items passes.
    const coachCells = table.locator('tbody tr td:last-child');
    const coachTexts = await coachCells.allInnerTexts();
    const namedCoachRows = coachTexts.filter((text) => text.trim() !== '').length;
    expect(
      namedCoachRows,
      'the seeded data must include at least one coached EDJEr',
    ).toBeGreaterThan(0);
    await expect(coachCells.getByRole('link')).toHaveCount(namedCoachRows);
    for (const href of await coachCells
      .getByRole('link')
      .evaluateAll((links) => links.map((link) => link.getAttribute('href') ?? ''))) {
      expect(href).toMatch(/^\/compass\/team-directory\/\d+$/);
    }

    // The client link still navigates — the original assertion, retargeted by href.
    const clientLink = table.locator('a[href^="/compass/client-directory/"]').first();
    await expect(clientLink).toBeVisible();
    const clientHref = await clientLink.getAttribute('href');
    expect(clientHref).toMatch(/^\/compass\/client-directory\/\d+$/);

    await clientLink.click();
    await expect(page).toHaveURL(new RegExp(`${clientHref}$`));
  });

  test('has no WCAG 2.1 AA violations at any of the five viewports (SC-010)', async ({ page }) => {
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });
    // Let the default breakdown finish loading before scanning — a "Loading…" status region is not
    // a violation, but scanning mid-fetch would race the assertion against the fetch itself.
    await expect(page.getByRole('table')).toBeVisible({ timeout: 15_000 });

    await expectNoWcagViolations(page, 'sales-dashboard');
  });
});
