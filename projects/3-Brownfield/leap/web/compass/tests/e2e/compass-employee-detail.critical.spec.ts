// The read-only employee detail (AC-8, AC-10, AC-11), reached the way AC-7 describes: from a Team
// Directory row, not by typing a URL.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';

async function signInAsBaselineEdjer(page: Page): Promise<void> {
  const response = await page.request.get(
    '/auth/stub-login?email=nadia.compassroles@example.test',
    {
      maxRedirects: 0,
    },
  );
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

/**
 * "The assignment history has arrived" — at ANY viewport.
 *
 * It is a `<table>` at >= `md` and a `<ul>` of cards below it (T078), with the SAME accessible name
 * either way, so this is one locator rather than a viewport branch. Used only as a readiness signal:
 * these tests are about what is absent from the page, and the history is what proves the page loaded
 * with data rather than being caught mid-fetch.
 *
 * ⚠️ Asserting the `table` role alone is what broke here — the compass device projects run at 390px and
 * 360px, so a table-only locator passes every PR on chromium and fails only on merge-to-main and the
 * Tuesday schedule.
 */
function assignmentHistory(page: Page) {
  const name = 'Assignment history';
  return page.getByRole('table', { name }).or(page.getByRole('list', { name }));
}

test.describe.configure({ mode: 'serial' });

test.describe('Compass employee detail [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsBaselineEdjer(page);
  });

  test('opens from a Team Directory row and offers no edit control', async ({ page }) => {
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    // AC-7's destination, reached through the EDJEr's own name (issue #245, superseding the trailing
    // View action of 2026-08-18, which itself superseded the mockup's email drill-in). The first data
    // row's first cell is the first-name link.
    await table.getByRole('row').nth(1).getByRole('link').first().click();

    await expect(assignmentHistory(page)).toBeVisible({ timeout: 15_000 });

    // AC-8: no edit controls, for this viewer.
    await expect(page.getByRole('button', { name: /edit|save|update|delete/i })).toHaveCount(0);
    await expect(page.getByRole('textbox')).toHaveCount(0);
  });

  test('withholds Time Tracking Settings from a baseline viewer (AC-10)', async ({ page }) => {
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });
    await table.getByRole('row').nth(1).getByRole('link').first().click();
    await expect(assignmentHistory(page)).toBeVisible({ timeout: 15_000 });

    await expect(page.getByText('Time Tracking Settings')).toHaveCount(0);
  });

  test('does not display the EDJEr’s email anywhere on the detail page (issue #245)', async ({
    page,
  }) => {
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });
    await table.getByRole('row').nth(1).getByRole('link').first().click();
    await expect(assignmentHistory(page)).toBeVisible({ timeout: 15_000 });

    await expect(page.getByText(/^email$/i)).toHaveCount(0);
    await expect(page.getByText(/@example\.test/)).toHaveCount(0);
  });

  test('reports a record the viewer may not see as not found, never as forbidden', async ({
    page,
  }) => {
    // FR-021: for a baseline viewer an inactive EDJEr must be indistinguishable from one that never
    // existed — ids are sequential, so a 403 would be a probe for existence.
    await page.goto('/compass/team-directory/999999');

    await expect(page.getByText(/not found/i)).toBeVisible({ timeout: 15_000 });
  });
});
