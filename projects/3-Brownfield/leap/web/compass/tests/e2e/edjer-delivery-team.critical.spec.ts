// The delivery-team flag, end to end: set it on the EDJEr form, read it back on that EDJEr's record.
// One create-and-see-it journey — wiring, not a CRUD cycle. What the flag does in the store, on each
// transport and at each tier is settled by the integration suite; this proves the screens are joined.
//
// Serial, with its own fixture identity.

import { expect, test, type Page } from '@playwright/test';

import { stubLogin } from './helpers/auth';

/**
 * The `<dd>` beside a `<dt>` in the record's profile list.
 *
 * A DOM locator rather than `getByRole('term')`: `dt`/`dd` map to the `term`/`definition` roles, which
 * the engines expose inconsistently, and this suite runs on five projects. Scoping is not optional —
 * "Yes" and "No" appear again in the Time Tracking Settings section on an elevated payload.
 */
function profileValue(page: Page, label: string) {
  return page
    .locator('dl > div')
    .filter({ has: page.locator('dt', { hasText: label }) })
    .locator('dd');
}

test.describe.configure({ mode: 'serial' });

test.describe('@compass the delivery-team flag [critical]', () => {
  test('an EDJEr added as NOT on the delivery team reads back that way', async ({ page }) => {
    await stubLogin(page.request, {
      email: 'delivery.flag@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });

    await page.goto('/compass/admin/edjers/new');

    const profile = page.getByRole('region', { name: 'Profile' });
    await expect(profile).toBeVisible({ timeout: 15_000 });

    // Turned OFF, deliberately. The server defaults this to true, so an EDJEr created with the toggle
    // left alone would read back "Yes" even if the form never sent the field at all — the affirmative
    // journey cannot fail. Turning it off is what makes the round trip observable.
    const toggle = profile.getByRole('switch', { name: /^Delivery Team/ });
    await expect(toggle).toHaveAttribute('aria-checked', 'true');
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');

    // Unique per run and per project: `serial` orders this file only within one project, and five
    // critical projects run it, so a bare timestamp can collide on ux_employee_email_ci. The refused
    // create leaves the form in place and surfaces as the waitForURL timeout below — which reads as a
    // save regression rather than a fixture clash. This spec creates a record the stack keeps until
    // its next reset.
    const rand = Math.random().toString(36).slice(2, 8);
    const email = `delivery.flag.${test.info().project.name}.${Date.now()}.${rand}@example.test`;
    await page.getByLabel(/first name/i).fill('Delivery');
    await page.getByLabel(/last name/i).fill('Flag');
    await page.getByLabel(/hire date/i).fill('2020-01-06');
    await page.getByLabel(/email address/i).fill(email);
    await page.getByLabel(/employee type/i).selectOption({ index: 1 });
    await page.getByLabel(/state of residence/i).selectOption('OH');
    await page.getByLabel(/timezone/i).selectOption('America/New_York');

    await page.getByRole('button', { name: 'Save EDJEr' }).click();

    // A successful add lands on the new record's own edit route, which is where its id comes from.
    await page.waitForURL(/\/compass\/admin\/edjers\/\d+$/, { timeout: 15_000 });
    const created = /\/edjers\/(\d+)$/.exec(page.url())?.[1];
    expect(created, 'the add did not land on a numbered record route').toBeTruthy();

    await page.goto(`/compass/team-directory/${created}`);
    await expect(page.getByRole('heading', { name: 'Delivery Flag' })).toBeVisible({
      timeout: 15_000,
    });

    await expect(profileValue(page, 'Delivery Team')).toHaveText('No');
  });
});
