// The Client Directory and the client view (AC-12 to AC-16) against real seeded data.
//
// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.

import { test, expect, type Page } from '@playwright/test';

async function signInAsBaselineEdjer(page: Page): Promise<void> {
  const response = await page.request.get('/auth/stub-login?email=blair.dev@example.test', {
    maxRedirects: 0,
  });
  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

test.describe.configure({ mode: 'serial' });

test.describe('Compass Client Directory [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsBaselineEdjer(page);
  });

  test('lists clients, each with a status that is never blank (AC-12)', async ({ page }) => {
    await page.goto('/compass/client-directory');

    await expect(page.getByRole('heading', { name: 'Client Directory' })).toBeVisible({
      timeout: 15_000,
    });

    const table = page.getByRole('table', { name: 'Client Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    const rows = table.getByRole('row');
    expect(await rows.count()).toBeGreaterThan(2);

    // Closed and total: every data row carries one of exactly three values (issue #274 added
    // `Former`). A blank would not sort, and totality is what the sortable column depends on.
    const statuses = (await table.locator('tbody tr td:nth-child(2)').allTextContents()).map((s) =>
      s.trim(),
    );
    expect(statuses.length).toBeGreaterThan(0);
    for (const status of statuses) {
      expect(['Active', 'Inactive', 'Former']).toContain(status);
    }

    // The value space alone would still pass if the server collapsed Former back into Inactive, so
    // assert the SPLIT is really present. The seeder makes both reachable on this screen, and the
    // distinction is the whole of #274: Halcyon Media Works (client 9) has never been assigned, and
    // Silverline Retail Partners (client 7) has two assignments that both closed.
    expect(statuses, 'a client that has never been assigned must read Inactive').toContain(
      'Inactive',
    );
    expect(statuses, 'a client whose assignments have all ended must read Former (#274)').toContain(
      'Former',
    );
  });

  test('opens a client view with the name shown, and no details panel for a baseline viewer', async ({
    page,
  }) => {
    await page.goto('/compass/client-directory');
    const table = page.getByRole('table', { name: 'Client Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    const firstClient = table.locator('tbody tr td:nth-child(1) a').first();
    const clientName = (await firstClient.textContent())?.trim() ?? '';
    await firstClient.click();

    // AC-13: the name is shown even though AC-14 hides the panel it would otherwise live in.
    await expect(page.getByRole('heading', { level: 1, name: clientName })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByRole('heading', { name: /client details/i })).toHaveCount(0);

    // AC-16: no View-SOW affordance for a baseline viewer.
    await expect(page.getByRole('link', { name: /view sow/i })).toHaveCount(0);
  });

  test('shows the same status on the client view as in the directory (SC-005)', async ({
    page,
  }) => {
    await page.goto('/compass/client-directory');
    const table = page.getByRole('table', { name: 'Client Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    const firstRow = table.locator('tbody tr').first();
    const statusInDirectory = (await firstRow.locator('td:nth-child(2)').textContent())?.trim();

    // Asserted rather than asserted-away with `!`: if the cell were empty this test would otherwise
    // "pass" by comparing an empty string against an empty string.
    expect(statusInDirectory).toMatch(/^(Active|Inactive|Former)$/);

    await firstRow.locator('td:nth-child(1) a').click();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 15_000 });

    // One derivation, two surfaces — they cannot disagree, and this is where that becomes observable.
    await expect(page.getByText(String(statusInDirectory), { exact: true })).toBeVisible();
  });
});
