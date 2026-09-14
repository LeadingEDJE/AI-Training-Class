// T097, RETARGETED for issue #337 (was `no-export-control.critical.spec.ts`).
//
// What changed and why this file was not deleted
// ----------------------------------------------
// It used to assert that NO export affordance existed on any of the four feature-007 screens — a
// build-by-omission guard for FR-022 / AC-NFR-6. Issue #337 asks for CSV export of the reports, so
// that requirement was amended rather than merely overridden: PRD v11's AC-NFR-6 forbids
// "PDF/print export" in as many words, and spec 007's FR-022 had widened that to CSV by inference
// (the spec says so itself: "the word 'CSV' appears in neither PRD v11 nor Plan v7").
//
// So the ban is narrowed, not lifted, and this file now asserts all three surviving halves:
//
//   1. the three REPORTS each expose a CSV export control      <- new, #337
//   2. the SALES DASHBOARD still exposes none                   <- unchanged; it is not a report
//   3. NO screen exposes a PDF or print affordance              <- unchanged; the literal PRD text
//
// Deleting the file and keeping only the happy path would have quietly dropped 2 and 3. A
// build-by-omission requirement whose guard is removed alongside the change that narrowed it is
// indistinguishable from one that was never enforced.
//
// The companion no-read-audit half of T097 lives in the integration suite
// (`CompassDashboardEndpointsTests.OpeningTheDashboard_WritesNoAuditRow`, its report twin, and
// `CompassReportExportEndpointsTests.ExportingAReport_WritesNoAuditRow` — an export is a read).

import { test, expect, type Page } from '@playwright/test';

/**
 * A PDF or print affordance, which remains forbidden on every screen (AC-NFR-6's literal text).
 *
 * Note what is NOT here any more: `export`, `download` and `csv`. Those are now expected on the
 * reports, so matching them would make this pattern assert the opposite of the requirement.
 */
const PRINT_CONTROL = /print|pdf|save as/i;

const REPORTS = [
  { route: '/compass/reports/availability', name: 'Availability Report' },
  { route: '/compass/reports/assignment-duration', name: 'Client Assignment Duration' },
  { route: '/compass/reports/assignment-start', name: 'Assignment Start' },
  { route: '/compass/reports/sow-extension', name: 'SOW Extension Report' },
] as const;

async function signInAsCompassSales(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=export.control@example.test&groups=Compass-Sales-dev',
    { maxRedirects: 0 },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

async function assertNoPrintOrPdfControl(page: Page): Promise<void> {
  await expect(
    page.getByRole('button', { name: PRINT_CONTROL }),
    'no print/PDF BUTTON may exist (AC-NFR-6)',
  ).toHaveCount(0);
  await expect(
    page.getByRole('link', { name: PRINT_CONTROL }),
    'no print/PDF LINK may exist (AC-NFR-6)',
  ).toHaveCount(0);
}

test.describe.configure({ mode: 'serial' });

test.describe('Report export controls [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsCompassSales(page);
  });

  // ---------------------------------------------------------------- 1. the reports DO export

  for (const report of REPORTS) {
    test(`${report.name} exposes a CSV export control`, async ({ page }) => {
      await page.goto(report.route);
      await expect(page.getByRole('link', { name: 'Availability Report' })).toBeVisible({
        timeout: 15_000,
      });

      // Assignment Start and SOW Extension both render their export only after a lookup has run —
      // before that there is no range to export. Run one so the control is reachable.
      if (report.route.endsWith('assignment-start') || report.route.endsWith('sow-extension')) {
        // ANCHORED, because `getByLabel` matches an `aria-label` too and since issue #461 the
        // Assignment Start Date header carries "Sort by assignment start date" — an unanchored
        // /start date/i matches the field AND that button once results are on screen, which is a
        // strict-mode violation rather than a wrong answer. Safe here only because this runs on a
        // fresh form; anchoring removes the ordering dependency instead of relying on it.
        await page.getByLabel(/^start date/i).fill('2000-01-01');
        await page.getByLabel('End Date').fill('2100-01-01');
        await page.getByRole('button', { name: 'Run' }).click();
      }

      // At least one: the Availability Report carries four (three sections plus Export All).
      const exports = page.getByRole('button', { name: /export/i });
      await expect(exports.first()).toBeVisible({ timeout: 15_000 });
    });
  }

  test('the Availability Report exports each section separately AND all three together', async ({
    page,
  }) => {
    // The shape issue #337 asked about explicitly. Three sections with different column sets cannot
    // honestly become one CSV, so the answer is three files plus a zip -- and that is a structural
    // claim worth asserting rather than a rendering detail.
    await page.goto('/compass/reports/availability');
    await expect(page.getByRole('heading', { name: '1 · Currently Available EDJErs' })).toBeVisible(
      {
        timeout: 15_000,
      },
    );

    // The per-section controls share a visible label, so they are told apart by the accessible
    // suffix -- which is the property a screen-reader user depends on (WCAG 2.4.6).
    for (const section of [
      'Currently available EDJErs',
      'Confirmed rollouts',
      'Unconfirmed SOWs expiring within 90 days',
    ]) {
      await expect(
        page.getByRole('button', { name: `Export CSV: ${section}` }),
        `the ${section} section needs its own distinguishable export control`,
      ).toBeVisible();
    }

    await expect(page.getByRole('button', { name: 'Export All (.zip)' })).toBeVisible();
  });

  test('an export actually downloads a file', async ({ page }) => {
    // The end-to-end proof. Everything above asserts a control EXISTS; this is the only assertion
    // that the control does the thing -- and the download goes through `apiFetch`, so it also proves
    // the session cookie survived the blob round trip.
    await page.goto('/compass/reports/assignment-duration');
    await expect(page.getByRole('heading', { name: 'Client Assignment Duration' })).toBeVisible({
      timeout: 15_000,
    });

    // Arm the listener BEFORE the click: the download can complete faster than the await, and a
    // listener attached afterwards misses the event and times out.
    const downloadPromise = page.waitForEvent('download', { timeout: 15_000 });
    await page.getByRole('button', { name: 'Export CSV' }).click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/^compass-assignment-duration-.*\.csv$/);
  });

  // ---------------------------------------------------------------- 2. the dashboard does NOT

  test('the Sales Dashboard still has no export control', async ({ page }) => {
    // Unchanged by #337, which is about REPORTS. The dashboard is a set of tiles over the same data
    // the reports expose, so adding an export here would be a new decision, not a consequence.
    await page.goto('/compass/sales-dashboard');
    await expect(page.getByRole('heading', { name: 'Sales Dashboard' })).toBeVisible({
      timeout: 15_000,
    });

    await expect(
      page.getByRole('button', { name: /export|download|csv/i }),
      'the dashboard is not a report: no export control (FR-022 as amended by #337)',
    ).toHaveCount(0);
    await assertNoPrintOrPdfControl(page);
  });

  // ---------------------------------------------------------------- 3. still no PDF or print

  for (const report of REPORTS) {
    test(`${report.name} has no print or PDF affordance`, async ({ page }) => {
      // AC-NFR-6's literal words. #337 narrowed the ban to this; it did not remove it.
      await page.goto(report.route);
      await expect(page.getByRole('link', { name: 'Availability Report' })).toBeVisible({
        timeout: 15_000,
      });
      await assertNoPrintOrPdfControl(page);
    });
  }
});
