// The Team Directory (AC-5, AC-6, AC-7). This is the first Compass screen backed by real data, so it
// is also the first place the whole chain is exercised end to end: SPA fallback -> client router ->
// read endpoint -> Postgres -> projection. Every assertion below fails for a different reason, which
// is the point of running it in a browser rather than trusting the unit suites.
//
// The E2E stack runs with DevBypass OFF and a synthetic @example.test identity, so the viewer resolves
// to tier BASELINE in Compass: Compass inherits nothing, not even from the timesheet roles the
// identity carries.

import { test, expect, type Page } from '@playwright/test';

/**
 * Establishes a real cookie session through the same CompleteSignIn pipeline every other E2E
 * identity uses.
 *
 * The E2E stack runs with DevBypass OFF, so without this the page renders but every API call is a
 * 401 — the directory looks empty and the navigation renders zero links. Driven through the Compass
 * dev server's own origin (the `/auth` proxy), because a cookie set on the API's origin is invisible
 * to a page served from this one.
 *
 * The identity carries no Compass group, which is deliberate: it resolves to tier BASELINE, and AC-5
 * grants the Team Directory to exactly that viewer.
 *
 * Serial, with its own fixture identity. Compass
 * identities are distinct per spec file by CONVENTION; no gate enforces it, because
 * `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.
 */
async function signInAsBaselineEdjer(page: Page): Promise<void> {
  const response = await page.request.get('/auth/stub-login?email=mira.compass@example.test', {
    maxRedirects: 0,
  });

  expect([302, 303], `stub-login must mint a session (got ${response.status()})`).toContain(
    response.status(),
  );
}

// Serial, with its own fixture identity. Compass
// identities are distinct per spec file by CONVENTION; no gate enforces it, because
// `e2e-fixture-isolation.test.ts` scans only `web/timesheet/tests/e2e`.
test.describe.configure({ mode: 'serial' });

test.describe('Compass Team Directory [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await signInAsBaselineEdjer(page);
  });

  test('lists seeded EDJErs with the AC-5 columns', async ({ page }) => {
    await page.goto('/compass/team-directory');

    await expect(page.getByRole('heading', { name: 'Team Directory' })).toBeVisible({
      timeout: 15_000,
    });

    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    // The mockups' own header labels — "Type", not "Employee type", for the fourth column.
    //
    // No 'Email': the owner replaced that column with the EDJEr's own name as the drill-in
    // (2026-08-19). The address is still on the wire and still on the EDJEr's own record; it is the
    // LISTING that no longer shows it. `Current Client(s)` gained a sort control in feature 008 and
    // is asserted with the rest.
    for (const column of [
      'First Name',
      'Last Name',
      'Hire Date',
      'Type',
      'Coach',
      'State',
      'Current Client(s)',
    ]) {
      await expect(
        table.getByRole('button', { name: `Sort by ${column.toLowerCase()}` }),
      ).toBeVisible();
    }

    // The address is gone from the header AND the body — asserted separately, because the loop above
    // would still pass if the column were rendered without a sort control.
    await expect(table.getByRole('button', { name: 'Sort by email' })).toHaveCount(0);
    await expect(table.getByRole('columnheader', { name: /email/i })).toHaveCount(0);

    // No trailing 'Actions' column either — the drill-in is the EDJEr's own name.
    await expect(table.getByRole('columnheader', { name: 'Actions' })).toHaveCount(0);

    // And the first row's name is itself the drill-in link (owner request 2026-08-19).
    await expect(
      table.getByRole('row').nth(1).getByRole('cell').first().getByRole('link'),
    ).toBeVisible();

    // The seeder produces ~90 EDJErs; anything less than a couple of rows means the query returned
    // nothing and every other assertion here would pass vacuously.
    expect(await table.getByRole('row').count()).toBeGreaterThan(2);
  });

  test('renders the hire date as mm/dd/yyyy rather than as the wire format', async ({ page }) => {
    // The projection sends a DateOnly (`2016-03-14`). Asserting the RENDERED form is what catches a
    // regression to raw ISO, which looks like data and reads like a bug.
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    await expect(table.getByText(/^\d{2}\/\d{2}\/\d{4}$/).first()).toBeVisible();
  });

  test('defaults to showing every EDJEr, unpaged (issue #247)', async ({ page }) => {
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    // The seeder produces ~90 EDJErs; the default is 'all', so the whole roster shows with no pager.
    await expect(page.getByText(/Showing 1 to \d+ of \d+ EDJErs/)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('navigation', { name: 'Pagination' })).toHaveCount(0);
    await expect(page.getByLabel('EDJErs per Page')).toHaveValue('all');
  });

  test('pages through the directory once a smaller size is chosen', async ({ page }) => {
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    await page.getByLabel('EDJErs per Page').selectOption('20');

    await expect(page.getByText(/Showing 1 to 20 of \d+ EDJErs/)).toBeVisible({ timeout: 15_000 });
    // 20 rows plus the header.
    expect(await table.getByRole('row').count()).toBe(21);

    const pager = page.getByRole('navigation', { name: 'Pagination' });
    await expect(pager.getByRole('button', { name: 'Previous' })).toBeDisabled();

    await pager.getByRole('button', { name: 'Next' }).click();

    await expect(page.getByText(/Showing 21 to 40 of \d+ EDJErs/)).toBeVisible();
    await expect(pager.getByRole('button', { name: 'Previous' })).toBeEnabled();
  });

  test('searches ACROSS pages, not within the page on screen', async ({ page }) => {
    // The failure this guards: paginate in the browser, filter in the browser, and a search run from
    // page 2 reports "no matches" for a surname sitting on page 1. Search goes to the server, which
    // searches the whole directory and answers with a fresh first page.
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });
    await page.getByLabel('EDJErs per Page').selectOption('20');

    // Take a surname from the FIRST page, then walk away from it.
    const surnameOnPageOne = (
      await table.getByRole('row').nth(1).getByRole('cell').nth(1).innerText()
    ).trim();
    await page
      .getByRole('navigation', { name: 'Pagination' })
      .getByRole('button', { name: 'Next' })
      .click();
    await expect(page.getByText(/Showing 21 to/)).toBeVisible();

    await page.getByLabel('Search by Last Name').fill(surnameOnPageOne);

    // Found, and shown from the first page of the new result set.
    await expect(table.getByRole('row').filter({ hasText: surnameOnPageOne }).first()).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByText(/Showing 1 to/)).toBeVisible();
  });

  test('narrows by a partial last-name search', async ({ page }) => {
    await page.goto('/compass/team-directory');
    const table = page.getByRole('table', { name: 'Team Directory' });
    await expect(table).toBeVisible({ timeout: 15_000 });

    const before = await table.getByRole('row').count();

    // Two characters that cannot match every surname. The assertion is that the set CHANGES and the
    // page survives — matching an exact seeded name would couple this spec to fixture content.
    await page.getByLabel('Search by Last Name').fill('zz');

    await expect
      .poll(
        async () =>
          page
            .getByText('No EDJErs match')
            .isVisible()
            .catch(() => false),
        {
          timeout: 15_000,
        },
      )
      .toBe(true);

    // And back again, proving the empty state is a state rather than a dead end.
    await page.getByLabel('Search by Last Name').fill('');
    await expect.poll(async () => table.getByRole('row').count(), { timeout: 15_000 }).toBe(before);
  });

  test('offers no status filter to a baseline viewer', async ({ page }) => {
    // FR-011a: the control is an elevated convenience. Its absence here is not the access control —
    // the server applies the entitlement clause regardless — but it should not be offered to someone
    // entitled to nothing.
    await page.goto('/compass/team-directory');
    await expect(page.getByRole('table', { name: 'Team Directory' })).toBeVisible({
      timeout: 15_000,
    });

    await expect(page.getByLabel('Status')).toHaveCount(0);
  });

  test('reaches the directory from the navigation shell', async ({ page }) => {
    // CompassNav has advertised this destination since Stream 1, when it led nowhere.
    await page.goto('/compass/');

    // Below `md` the nav links live behind a disclosure button (owner request, 2026-08-25), so the
    // link is not in the DOM until it is opened. Conditional rather than viewport-branched, so this
    // reads the same in every project.
    //
    // ⚠️ COPY THIS SHAPE for any future spec that navigates through the nav — INCLUDING the wait on
    // the first line, which is load-bearing and was missing until this test broke `main`.
    //
    // `isVisible()` does NOT auto-wait; it answers about the DOM at the instant it is called. Neither
    // rendering of this nav exists at that instant: `CompassNav` renders no links and no Menu button
    // until `useCurrentUser()` — a react-query fetch — resolves, because "zero permitted links renders
    // NO button". `page.goto()` settles at `load`, well before that. Branching on a bare `isVisible()`
    // therefore reads `false` from a nav that has not rendered yet, skips opening the menu, and then
    // waits the full timeout for a link sitting inside a panel nothing opened.
    //
    // Waiting for whichever form THIS viewport renders makes the branch answer a settled question.
    // Desktop hid the bug: there the links are in the bar, so `false` is the correct answer and
    // `click()`'s own auto-waiting covers the rest of the race. Only the 390px and 360px projects
    // failed, and those run on merge-to-main and the Tuesday schedule — hours later, in a job nobody
    // is watching.
    const link = page.getByRole('link', { name: 'Team Directory' });
    const menuButton = page.getByRole('button', { name: 'Menu' });

    await expect(link.or(menuButton).first()).toBeVisible({ timeout: 15_000 });

    if (!(await link.isVisible())) {
      await menuButton.click();
    }

    await link.click();

    await expect(page.getByRole('heading', { name: 'Team Directory' })).toBeVisible({
      timeout: 15_000,
    });
  });
});
