// The Availability Report (AC-38, BR-5; J19; RPT-3) against real seeded data — the three sections, the
// addressable-URL behaviour a tab strip has to have, the server-side denial, and the axe-core sweep at
// the five AC-NFR-7 viewports.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';

import { expectNoWcagViolations } from './helpers/axe';

const AVAILABILITY_PATH = '/compass/reports/availability';

async function signInAsCompassOps(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=availability.report@example.test&groups=Compass-Ops-dev',
    { maxRedirects: 0 },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

/**
 * Row count of a section, excluding the header row — and **0 for a section that is legitimately empty**.
 *
 * `Table` renders its empty message INSTEAD of the table, so waiting for the table would turn an empty
 * section into a 15-second timeout reading "the report did not render". The SC-003 equalities are true
 * and worth checking at zero (0 available EDJErs must equal a beach tile of 0), so this waits for
 * EITHER the table or the empty message and reports the count.
 */
async function sectionRowCount(page: Page, accessibleName: string): Promise<number> {
  const table = page.getByRole('table', { name: accessibleName });
  const empty = page.getByText(emptyMessageFor(accessibleName), { exact: true });

  await expect(table.or(empty).first()).toBeVisible({ timeout: 15_000 });

  return (await table.count()) === 0 ? 0 : (await table.getByRole('row').count()) - 1;
}

/** The empty-state copy each section renders when it has no rows, keyed by the table's caption. */
function emptyMessageFor(accessibleName: string): string {
  const messages: Record<string, string> = {
    'Currently available EDJErs': 'No EDJErs are currently available.',
    'Confirmed rollouts': 'No confirmed rollouts.',
    'Unconfirmed SOWs expiring within 90 days':
      'No unconfirmed SOWs are expiring in the next 90 days.',
  };

  const message = messages[accessibleName];
  if (message === undefined) {
    throw new Error(`no empty-state copy registered for section "${accessibleName}"`);
  }

  return message;
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

test.describe('Availability Report [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsCompassOps(page);
  });

  test('renders three labelled sections in the stated order (AC-38, J19)', async ({ page }) => {
    await page.goto(AVAILABILITY_PATH);

    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    // The mockup's own section labels, in RPT-3's order. Order is asserted as a SEQUENCE rather than
    // three independent visibility checks: three sections that all render in the wrong order would
    // pass three separate assertions.
    //
    // level 3, and an h2 naming the report above them: RPT-3 is ONE card with an h2 and three h3
    // subheadings, not three sibling cards.
    await expect(
      page.getByRole('heading', { level: 2, name: 'Availability Report' }),
    ).toBeVisible();
    await expect(page.getByRole('heading', { level: 3 })).toHaveText([
      '1 · Currently Available EDJErs',
      '2 · Confirmed Rollouts',
      '3 · Unconfirmed SOWs (expiring in next 90 days)',
    ]);
  });

  test('gives section 1 four columns and sections 2 and 3 six each (RPT-3, FR-029, issues #334, #386)', async ({
    page,
  }) => {
    await page.goto(AVAILABILITY_PATH);

    const available = page.getByRole('table', { name: 'Currently available EDJErs' });
    await expect(available).toBeVisible({ timeout: 15_000 });

    // FOUR. `EDJE Client Assignment Start` is one date column, not a client column plus a date —
    // FR-029 once read it as four for that reason and that was a misread of the mockup (spec
    // Finding 10, T050). `# of Days Available` is a separate, later, owner-requested column
    // (issue #334, spec Deviation 11) and is not a relapse of that misread. Section 1 carries no
    // employee type (issue #386 only added it to §2/§3, matching the source data), so it stays four.
    await expect(available.getByRole('columnheader')).toHaveText([
      'EDJEr',
      'EDJE Client Assignment Start',
      '# of Days Available',
      'Coach',
    ]);

    // The `#` prefix is the mockup's on these two, and deliberately absent on the dashboard's own
    // `Days Until Expiration`. Do not normalise the two screens to one spelling (T102).
    //
    // Employee Type is now the SECOND column in both §2 and §3, right after EDJEr — it used to be an
    // inline badge inside the EDJEr cell (issue #334), and issue #386 gave it its own column instead.
    const confirmedRollouts = page.getByRole('table', { name: 'Confirmed rollouts' });
    await expect(confirmedRollouts.getByRole('columnheader')).toHaveText([
      'EDJEr',
      'Employee Type',
      'Current Client',
      'Assignment End Date',
      '# Days Until Rollout',
      'Coach',
    ]);

    const unconfirmedSows = page.getByRole('table', {
      name: 'Unconfirmed SOWs expiring within 90 days',
    });
    await expect(unconfirmedSows.getByRole('columnheader')).toHaveText([
      'EDJEr',
      'Employee Type',
      'Current Client',
      'Current SOW End',
      '# Days Until SOW Expiration',
      'Coach',
    ]);

    // Prove the new column carries real data, not just a header (issue #386) — the second cell of the
    // first data row in each section. Employee type is required in practice on every seeded EDJEr, so
    // an empty cell here would mean the column exists but nothing populates it.
    await expect(
      confirmedRollouts.locator('tbody tr').first().locator('td').nth(1),
    ).not.toBeEmpty();
    await expect(unconfirmedSows.locator('tbody tr').first().locator('td').nth(1)).not.toBeEmpty();
  });

  // The tab strip is asserted in TWO places and both must move together: this browser assertion and
  // the jsdom twin at `tests/unit/features/reports/ReportsLayout.test.tsx`. Issue #534 updated the
  // unit one and missed this one, which is why the tab list is spelled out here rather than counted —
  // a count would have gone green on the wrong four labels.
  test('renders exactly the four built report tabs — RPT-2 is still not one of them (Finding 4)', async ({
    page,
  }) => {
    await page.goto(AVAILABILITY_PATH);

    const tabs = page.getByRole('navigation', { name: 'Reports' }).getByRole('link');
    await expect(tabs).toHaveText([
      'Availability Report',
      'Client Assignment Duration',
      'Assignment Start',
      'SOW Extension Report',
    ]);
    // The negative is the point of the test and survives the fourth tab: the mockup's "SOWs Expiring
    // in 90 Days" is RPT-2, which no acceptance criterion covers. "SOW Extension Report" (issue #534)
    // answers a different question and was requested directly, so it is not RPT-2 restored.
    await expect(page.getByText('SOWs Expiring in 90 Days')).toHaveCount(0);
  });

  test('deep-links to the report and survives a reload on it (FR-023)', async ({ page }) => {
    // The property that fails a tab strip built on local state: arriving directly at the tab's URL,
    // and staying there across a reload. `useState` tabs give all three reports one URL, so a deep
    // link lands on the first tab and a reload silently resets to it.
    await page.goto(AVAILABILITY_PATH);
    await expect(page.getByRole('heading', { name: '1 · Currently Available EDJErs' })).toBeVisible(
      {
        timeout: 15_000,
      },
    );

    await page.reload();

    expect(new URL(page.url()).pathname).toBe(AVAILABILITY_PATH);
    await expect(page.getByRole('heading', { name: '1 · Currently Available EDJErs' })).toBeVisible(
      {
        timeout: 15_000,
      },
    );

    // And the active tab is derived from the URL, so a deep link highlights the same tab a click would.
    await expect(
      page
        .getByRole('navigation', { name: 'Reports' })
        .getByRole('link', { name: 'Availability Report' }),
    ).toHaveAttribute('aria-current', 'page');
  });

  test('renders the Availability Report by default at /compass/reports, in place (FR-023)', async ({
    page,
  }) => {
    await page.goto('/compass/reports');

    // Resolves rather than falling through to the not-found page, which is FR-023's requirement -- and it
    // resolves to the DEFAULT report rather than a chooser (owner decision, 2026-08-19).
    await expect(page.getByRole('heading', { level: 2, name: 'Availability Report' })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByText(/not found/i)).toHaveCount(0);

    // Rendered IN PLACE, not redirected: the address the user typed survives. A redirect would rewrite
    // the address bar and add a history entry so Back appears not to work, which is why
    // `adminIndexRoute` rejected one for `/compass/admin` and why this route follows it.
    expect(new URL(page.url()).pathname).toBe('/compass/reports');

    // Back returns where they came from in ONE press -- the property a redirect breaks.
    await page.goto('/compass/sales-dashboard');
    await page.goto('/compass/reports');
    await page.goBack();
    expect(new URL(page.url()).pathname).toBe('/compass/sales-dashboard');
  });

  test('marks the Availability Report tab current at the bare /compass/reports (owner report)', async ({
    page,
  }) => {
    // The reported symptom: a report was plainly on screen and no tab was underlined. Fixed by
    // resolving the index pathname to what the index renders, NOT by redirecting -- the test above
    // still holds, so the address bar and single-press Back are intact.
    await page.goto('/compass/reports');

    const nav = page.getByRole('navigation', { name: 'Reports' });
    await expect(nav.locator('a[aria-current="page"]')).toHaveCount(1, { timeout: 15_000 });
    await expect(nav.locator('a[aria-current="page"]')).toHaveText('Availability Report');

    // The underline is the visible half of the same fact, and it is what was actually reported. The
    // active tab takes the EDJE green bottom border; an inactive one is transparent.
    const activeBorder = await nav
      .locator('a[aria-current="page"]')
      .evaluate((el) => getComputedStyle(el).borderBottomColor);
    expect(activeBorder).not.toBe('rgba(0, 0, 0, 0)');
    expect(activeBorder).not.toBe('transparent');
  });

  test('constrains the report to the standard page container, not the full viewport', async ({
    page,
  }) => {
    // The report was the only Compass screen with no max width and no padding, so its tables ran the
    // full width of a 1920px viewport while the mockup constrains `.page`. Asserted numerically because
    // it is invisible at 1280px and obvious at 1920px.
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto(AVAILABILITY_PATH);
    await expect(page.getByRole('heading', { level: 2, name: 'Availability Report' })).toBeVisible({
      timeout: 15_000,
    });

    const width = await page
      .locator('main')
      .first()
      .evaluate((el) => el.clientWidth);

    expect(width).toBeLessThanOrEqual(1024);
  });

  test('agrees with the dashboard on both shared populations (SC-003)', async ({ page }) => {
    // The Phase 4 checkpoint, in a browser: §1 is the beach tile's population and §2 the
    // confirmed-rollouts tile's (FR-005/FR-010, FR-004/FR-011). T043 pins this at the API; this pins
    // that what a user actually SEES agrees, which is the claim SC-003 makes.
    await page.goto(AVAILABILITY_PATH);
    const availableRows = await sectionRowCount(page, 'Currently available EDJErs');
    const rolloutRows = await sectionRowCount(page, 'Confirmed rollouts');

    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('button', { name: /EDJErs on the Beach/ })).toBeVisible({
      timeout: 15_000,
    });

    const beachTile = await page.getByRole('button', { name: /EDJErs on the Beach/ }).innerText();
    const rolloutTile = await page.getByRole('button', { name: /Confirmed Rollouts/ }).innerText();

    expect(Number((beachTile.match(/\d+/) ?? ['-1'])[0])).toBe(availableRows);
    expect(Number((rolloutTile.match(/\d+/) ?? ['-1'])[0])).toBe(rolloutRows);
  });

  test('has no WCAG 2.1 AA violations at any of the five viewports (SC-010)', async ({ page }) => {
    await page.goto(AVAILABILITY_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });
    // Scan after the fetch settles — a "Loading…" status region is not a violation, but scanning
    // mid-fetch would race the assertion against the fetch.
    await expect(page.getByRole('table', { name: 'Currently available EDJErs' })).toBeVisible({
      timeout: 15_000,
    });

    await expectNoWcagViolations(page, 'reports-availability');
  });
});
