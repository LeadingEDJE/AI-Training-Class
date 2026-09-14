// The Assignment Start lookup (AC-40; J21; RPT-5) against real seeded data — the range form, the
// inclusive-at-both-ends filtering, the rejected range that issues no query, the explicit empty state,
// the server-side denial, and the axe-core sweep at the five AC-NFR-7 viewports.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';

import { expectNoWcagViolations } from './helpers/axe';

const ASSIGNMENT_START_PATH = '/compass/reports/assignment-start';
const RESULTS_TABLE = 'Assignments that started within the selected date range';
const EMPTY_MESSAGE = 'No assignments started in that date range.';

async function signInAsCompassOps(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=assignment.start@example.test&groups=Compass-Ops-dev',
    { maxRedirects: 0 },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

const COLUMN_LABELS = ['EDJEr Name', 'Client Name', 'Assignment Start Date'] as const;

/**
 * The Start Date FIELD.
 *
 * Anchored, because `getByLabel` also matches an `aria-label` and since issue #461 the Assignment
 * Start Date header carries "Sort by assignment start date" — so once results are on screen an
 * unanchored `/start date/i` matches the field AND that button, which is a strict-mode violation
 * rather than a wrong answer.
 */
function startDateField(page: Page) {
  return page.getByLabel(/^start date/i);
}

/** A column's sort control, by the accessible name `SortButton` gives it (issue #461). */
function sortControl(page: Page, label: string) {
  return page.getByRole('button', { name: `Sort by ${label.toLowerCase()}` });
}

/**
 * A column's header, located by the BUTTON IT CONTAINS rather than by its own accessible name.
 *
 * `SortButton` carries an explicit `aria-label`, which REPLACES its text content when a browser
 * computes a name — and a `th`'s name comes from its contents. So in Chromium this header is named
 * "Sort by edjer name", not "EDJEr Name", while jsdom computes it from the text and disagrees, which
 * is why the unit suite is happy with a query this file should not rely on. Recorded at length in
 * `sales-dashboard.critical.spec.ts` (issue #464), where the disagreement was found the expensive way.
 *
 * **The loose string form still happens to match, and that is exactly why it is not used here.**
 * `getByRole('columnheader', { name: 'EDJEr Name' })` passes in Chromium — a string `name` matches a
 * case-insensitive SUBSTRING, and "sort by edjer name" contains "edjer name" — so it says nothing
 * about the label the header actually exposes. `{ exact: true }` and the regex form (`/EDJEr Name/`)
 * match nothing at all. Containment sidesteps the whole question rather than resting on which of
 * those three forms was written.
 */
function columnHeader(page: Page, label: string) {
  return page.locator('thead th').filter({ has: sortControl(page, label) });
}

/** Fills the range and runs the lookup. Dates are `yyyy-MM-dd` — the native date input's wire format. */
async function runLookup(page: Page, from: string, to: string): Promise<void> {
  await startDateField(page).fill(from);
  await page.getByLabel(/end date/i).fill(to);
  await page.getByRole('button', { name: 'Run' }).click();
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

test.describe('Assignment Start lookup [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsCompassOps(page);
  });

  test('shows the range form and NO results before a lookup is run (J21)', async ({ page }) => {
    await page.goto(ASSIGNMENT_START_PATH);

    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });
    await expect(startDateField(page)).toBeVisible();
    await expect(page.getByLabel(/end date/i)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Run' })).toBeVisible();

    // "You have not asked yet" is not "your range matched nothing". Neither the table nor the empty
    // state may appear before the user has chosen a range.
    await expect(page.getByRole('table', { name: RESULTS_TABLE })).toHaveCount(0);
    await expect(page.getByText(EMPTY_MESSAGE)).toHaveCount(0);
  });

  test('returns assignments started in the range, with the RPT-5 columns (AC-40, issue #386)', async ({
    page,
  }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    // A range wide enough to cover the seeded directory's assignments whatever the business date.
    await runLookup(page, '2000-01-01', '2099-12-31');

    const table = page.getByRole('table', { name: RESULTS_TABLE });
    await expect(table).toBeVisible({ timeout: 15_000 });

    for (const label of COLUMN_LABELS) {
      await expect(
        columnHeader(page, label),
        `${label} must be a column of this table`,
      ).toBeVisible();
    }

    // Employee Type is brand new on this report (issue #386) — added as a column right after the
    // EDJEr name, matching the Availability and Client Assignment Duration reports. Unlike the three
    // headers above, it carries no `SortButton` and so no overriding aria-label -- its accessible
    // name is its own text, and a direct name match is safe here (see `columnHeader`'s doc comment
    // for why that is NOT true of the sortable headers).
    await expect(table.getByRole('columnheader', { name: 'Employee Type' })).toBeVisible();

    // Header row plus at least one data row — the seed always carries assignments.
    expect(await table.getByRole('row').count()).toBeGreaterThan(1);

    // Prove the column carries a real value, not just a header — the second cell (Employee Type) of
    // the first data row. Every seeded EDJEr has an employee type in practice, so an empty cell here
    // would mean the column renders but nothing populates it.
    await expect(table.locator('tbody tr').first().locator('td').nth(1)).not.toBeEmpty();
  });

  // Issue #461. One browser test, for the two claims the unit suite cannot make: that `aria-sort`
  // lands on the real header a screen reader reads (jsdom disagrees with Chromium about a `th`'s
  // name, per `columnHeader` above), and that a click re-orders rows already on the page instead of
  // asking the server again. Ordering itself is asserted per column in
  // `tests/unit/features/reports/AssignmentStartReport.test.tsx` — this is wiring, not a second copy
  // of it.
  test('orders by every header, client-side, without re-querying (#461)', async ({ page }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    const requests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/compass/reports/assignment-start')) {
        requests.push(request.url());
      }
    });

    await runLookup(page, '2000-01-01', '2099-12-31');

    const table = page.getByRole('table', { name: RESULTS_TABLE });
    await expect(table).toBeVisible({ timeout: 15_000 });
    const rowCount = await table.getByRole('row').count();
    expect(requests, 'the lookup itself is exactly one request').toHaveLength(1);

    for (const label of COLUMN_LABELS) {
      await sortControl(page, label).click();

      await expect(
        columnHeader(page, label),
        `${label} must report its sort state on the header`,
      ).toHaveAttribute('aria-sort', /ascending|descending/);
    }

    // The ordering changed three times and the data set did not.
    expect(await table.getByRole('row').count()).toBe(rowCount);
    expect(requests, 'sorting is client-side — no header click may re-query').toHaveLength(1);
  });

  test('renders start dates as mm/dd/yyyy, never a raw ISO string (#234)', async ({ page }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    await runLookup(page, '2000-01-01', '2099-12-31');

    const table = page.getByRole('table', { name: RESULTS_TABLE });
    await expect(table).toBeVisible({ timeout: 15_000 });

    const body = (await table.textContent()) ?? '';
    expect(body, 'a date cell must render mm/dd/yyyy').toMatch(/\d{2}\/\d{2}\/\d{4}/);
    // The ISO form asserted ABSENT as well as the formatted one present — without this half, a cell
    // rendering both would pass.
    expect(body, 'no cell may render the raw ISO date').not.toMatch(/\d{4}-\d{2}-\d{2}/);
  });

  test('rejects an inverted range with a message and issues NO request (US4 scenario 2)', async ({
    page,
  }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    // Watch the wire, because the requirement is about the request and not only the message: a build
    // that fetched and discarded the answer would satisfy a message-only assertion.
    const requests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/compass/reports/assignment-start')) {
        requests.push(request.url());
      }
    });

    await runLookup(page, '2026-03-31', '2026-03-01');

    await expect(page.getByText('The start date must be on or before the end date.')).toBeVisible();
    expect(requests, 'an inverted range must not reach the server').toHaveLength(0);
    await expect(page.getByRole('table', { name: RESULTS_TABLE })).toHaveCount(0);
  });

  test('shows an explicit empty state for a range matching nothing (US4 scenario 3)', async ({
    page,
  }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    // Far enough out that no seeded assignment can start inside it.
    await runLookup(page, '2090-01-01', '2090-12-31');

    await expect(page.getByText(EMPTY_MESSAGE)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('table', { name: RESULTS_TABLE })).toHaveCount(0);
  });

  test('deep-links to the lookup and survives a reload on it (FR-023)', async ({ page }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    await page.reload();

    await expect(startDateField(page)).toBeVisible({ timeout: 15_000 });
    expect(new URL(page.url()).pathname).toBe(ASSIGNMENT_START_PATH);
  });

  test('has no WCAG 2.1 AA violations at any of the five viewports (SC-010)', async ({ page }) => {
    await page.goto(ASSIGNMENT_START_PATH);
    await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible({ timeout: 15_000 });

    // Scan with RESULTS on screen: the table, not the bare form, is the part with the most to get
    // wrong, and scanning mid-fetch would race the assertion against the fetch.
    await runLookup(page, '2000-01-01', '2099-12-31');
    await expect(page.getByRole('table', { name: RESULTS_TABLE })).toBeVisible({ timeout: 15_000 });

    await expectNoWcagViolations(page, 'reports-assignment-start');
  });
});
