// The Client Assignment Duration report (AC-39, BR-16; J20; RPT-4) against real seeded data — the
// mockup's four columns plus Employee Type as its own fifth (issue #386), longest-first ordering,
// re-sorting without a re-fetch, the per-pair grain the seeded left-and-returned EDJEr exercises,
// the server-side denial, and the axe-core sweep at the five AC-NFR-7 viewports.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';

import { expectNoWcagViolations } from './helpers/axe';

const DURATION_PATH = '/compass/reports/assignment-duration';
const TABLE_NAME = 'Client assignment duration, longest tenure first';

async function signInAsCompassSales(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=duration.report@example.test&groups=Compass-Sales-dev',
    { maxRedirects: 0 },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

/** The rendered duration column, top to bottom. */
async function durationColumn(page: Page): Promise<string[]> {
  const rows = page.getByRole('table', { name: TABLE_NAME }).locator('tbody tr');
  await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  return rows.locator('td:nth-child(5)').allInnerTexts();
}

/** The day count inside a `"3 yrs 4 mos (1,238 days)"` cell. */
function days(display: string): number {
  const match = /\(([\d,]+) days?\)/.exec(display) ?? /^([\d,]+) days?$/.exec(display);
  if (match === null) {
    throw new Error(`"${display}" does not carry a parseable day count (FR-030)`);
  }

  return Number(match[1].replace(/,/g, ''));
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

test.describe('Client Assignment Duration [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsCompassSales(page);
  });

  test('renders the mockup RPT-4 columns in order (AC-39, CHK019, issue #386)', async ({
    page,
  }) => {
    await page.goto(DURATION_PATH);

    const headers = page.getByRole('table', { name: TABLE_NAME }).locator('thead th');
    await expect(headers).toHaveCount(5, { timeout: 15_000 });

    // Coach is the fourth column, and it is the one six artifacts named while no build task did
    // until this phase's tasks were corrected. Employee Type sits second, right after EDJEr
    // (issue #386) — asserted here so the browser confirms it, not only a unit test.
    await expect(headers.nth(0)).toContainText('EDJEr');
    await expect(headers.nth(1)).toContainText('Employee Type');
    await expect(headers.nth(2)).toContainText('Client');
    await expect(headers.nth(3)).toContainText('Coach');
    await expect(headers.nth(4)).toContainText('Duration');
  });

  test('orders longest tenure first by default (J20, FR-014)', async ({ page }) => {
    await page.goto(DURATION_PATH);

    const counts = (await durationColumn(page)).map(days);
    expect(counts.length).toBeGreaterThan(1);

    const descending = [...counts].sort((a, b) => b - a);
    expect(counts, 'the report must arrive ordered by total duration descending').toEqual(
      descending,
    );
  });

  test('marks Duration as the descending ordered column (RPT-4 caret)', async ({ page }) => {
    await page.goto(DURATION_PATH);
    const table = page.getByRole('table', { name: TABLE_NAME });
    await expect(table).toBeVisible({ timeout: 15_000 });

    await expect(table.locator('thead th').nth(4)).toHaveAttribute('aria-sort', 'descending');
  });

  test('re-sorts on a column without changing the data set (FR-014)', async ({ page }) => {
    await page.goto(DURATION_PATH);
    const before = await durationColumn(page);

    await page.getByRole('button', { name: 'Sort by duration' }).click();
    const after = await durationColumn(page);

    // Same rows, reversed. FR-014: "the ordering changes and the data set is unchanged" — so this
    // asserts the SET is identical rather than merely that something moved.
    expect(after).toHaveLength(before.length);
    expect([...after].sort()).toEqual([...before].sort());
    expect(after).toEqual([...before].reverse());
  });

  test('shows the left-and-returned EDJEr once, with combined tenure (FR-015)', async ({
    page,
  }) => {
    await page.goto(DURATION_PATH);
    const table = page.getByRole('table', { name: TABLE_NAME });
    await expect(table).toBeVisible({ timeout: 15_000 });

    // Seeded assignments 13 and 20 are both Ingrid Halvorsen to the same client (T057b/T016): an
    // earlier closed engagement plus the current one. The grain is the PAIR, so this is ONE row.
    const ingrid = table.locator('tbody tr', { hasText: 'Ingrid Halvorsen' });
    await expect(ingrid).toHaveCount(1);

    // The current leg alone is roughly 425 days; the combined tenure is materially more. A sum that
    // ignored the closed leg would land near the lower figure.
    const display = await ingrid.locator('td:nth-child(5)').innerText();
    expect(days(display)).toBeGreaterThan(500);
  });

  test('renders every duration in the mockup format (FR-030)', async ({ page }) => {
    await page.goto(DURATION_PATH);

    for (const display of await durationColumn(page)) {
      // Either "3 yrs 4 mos (1,238 days)" or, under a month, the bare "9 days".
      expect(display, `"${display}" is not FR-030's format`).toMatch(
        /^(\d+ yrs?( \d+ mos?)?|\d+ mos?) \([\d,]+ days?\)$|^[\d,]+ days?$/,
      );
    }
  });

  test('deep-links to the report and survives a reload on it (FR-023)', async ({ page }) => {
    await page.goto(DURATION_PATH);
    await expect(page.getByRole('table', { name: TABLE_NAME })).toBeVisible({ timeout: 15_000 });

    await page.reload();

    await expect(page.getByRole('table', { name: TABLE_NAME })).toBeVisible({ timeout: 15_000 });
    expect(new URL(page.url()).pathname).toBe(DURATION_PATH);
  });

  test('never lists the internal ("beach") client (issue #335)', async ({ page }) => {
    await page.goto(DURATION_PATH);
    const table = page.getByRole('table', { name: TABLE_NAME });
    await expect(table).toBeVisible({ timeout: 15_000 });

    // The seed holds several ACTIVE assignments at "Leading EDJE (Internal)" (employees 1, 2, 9, 10);
    // this report is scoped to external client assignments only, so none may surface here.
    await expect(table.locator('tbody tr', { hasText: 'Leading EDJE (Internal)' })).toHaveCount(0);
  });

  test("shows the EDJEr's employee type as its own column (issue #386)", async ({ page }) => {
    await page.goto(DURATION_PATH);
    const table = page.getByRole('table', { name: TABLE_NAME });
    await expect(table).toBeVisible({ timeout: 15_000 });

    // Ingrid Halvorsen (seeded employee 12) is Full Time — the same row used above for the
    // left-and-returned FR-015 case.
    const ingrid = table.locator('tbody tr', { hasText: 'Ingrid Halvorsen' });
    await expect(ingrid).toHaveCount(1);

    // Five columns now, and Employee Type is specifically the SECOND cell — checking that cell
    // directly (not just that the text appears somewhere in the row) is what proves it is its own
    // column rather than still trailing the name (issue #386, which replaced #335's inline badge).
    await expect(table.locator('thead th')).toHaveCount(5);
    await expect(ingrid.locator('td:nth-child(2)')).toContainText('Full Time');
  });

  test('tells a Compass Admin the report is not theirs to view (FR-019)', async ({ page }) => {
    // The SERVER refusal is asserted at the layer that can sweep every route at once --
    // CompassReportEndpointsTests.EveryReportRoute_AdmitsPermittedRoles_AndRefusesCompassAdmin.
    // What only a browser can show is that the refusal reaches the reader as a message rather
    // than a blank page (TEST-STRATEGY rule 1).
    //
    // Its own identity, per the fixture-isolation rule.
    const login = await page.request.get(
      '/auth/stub-login?email=duration.admin@example.test&groups=Compass-Admin-dev',
      { maxRedirects: 0 },
    );
    expect([302, 303]).toContain(login.status());

    await page.goto(DURATION_PATH);
    await expect(
      page.getByText('You do not have permission to view the Client Assignment Duration report.'),
    ).toBeVisible({ timeout: 15_000 });
  });

  test('has no WCAG 2.1 AA violations at any of the five viewports (SC-010)', async ({ page }) => {
    await page.goto(DURATION_PATH);
    // Scan after the fetch settles — a "Loading…" status region is not a violation, but scanning
    // mid-fetch would race the assertion against the fetch.
    await expect(page.getByRole('table', { name: TABLE_NAME })).toBeVisible({ timeout: 15_000 });

    await expectNoWcagViolations(page, 'reports-assignment-duration');
  });
});
