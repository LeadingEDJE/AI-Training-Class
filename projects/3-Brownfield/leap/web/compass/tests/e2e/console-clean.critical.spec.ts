// Feature 011 US5 (issue #83, T062/T065) — no Compass screen logs a console error in local dev.
//
// **Why this is worth a gate rather than a shrug.** Before this, every Compass screen logged a 404 for
// `/compass/config.js`: `index.html` references `/config.js` root-absolutely (correct — deployed
// environments serve it from nginx's synthesized `location = /config.js`), and Vite's DEV SERVER
// rewrites a root-absolute script `src` to the app base. Never a production defect. But the audit
// behind this feature had to positively rule that error out on all 22 screens before it could trust any
// console signal, and an error present everywhere is precisely what hides the next real one.
import { expect, test } from '@playwright/test';

import { stubLogin } from './helpers/auth';
import {
  EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT,
  compassScreens,
  resolveSeededIds,
  type SeededIds,
} from './helpers/compass-routes';

/**
 * One allowance per navigation, for the same reason and at the same size as
 * `responsive-stranding.critical.spec.ts`'s `NAVIGATION_ALLOWANCE_MS` -- read the comment there for
 * the full derivation.
 *
 * This spec walks the SAME `compassScreens()` inventory and allows each load up to 15 s
 * (`waitUntil: 'networkidle'`), so Playwright's default 30 s per TEST was never a budget it could
 * meet: two slow loads exhaust it on their own and there are twenty-five. It therefore failed as a
 * bare `Test timeout of 30000ms exceeded` naming no screen and asserting nothing about the console
 * -- which is the worst possible shape for a gate whose entire job is to surface a named error.
 * Reproduced on `main` with this spec alone, so it is a budget defect, not a product one.
 *
 * `responsive-stranding` was given this treatment and this walker was missed; the two are now in
 * step. It does not weaken the gate: a genuinely stuck navigation still throws on the per-`goto`
 * 15 s cap below, well inside this budget, and names the screen when it does.
 */
const NAVIGATION_ALLOWANCE_MS = 4_800;
const INVENTORY_WALK_TIMEOUT_MS = EXPECTED_SCREEN_COUNT_WITH_ASSIGNMENT * NAVIGATION_ALLOWANCE_MS;

test.describe.configure({ mode: 'serial' });

test.describe('@compass the console is clean on every screen [critical]', () => {
  let ids: SeededIds;

  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage();
    await stubLogin(page.request, {
      email: 'console.sweep@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });
    ids = await resolveSeededIds(page);
    await page.close();
  });

  test('no screen emits a console error or a page error', async ({ page }) => {
    test.setTimeout(INVENTORY_WALK_TIMEOUT_MS);

    await stubLogin(page.request, {
      email: 'console.sweep@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });

    const problems: string[] = [];
    let current = '(before navigation)';

    page.on('console', (message) => {
      if (message.type() === 'error') {
        problems.push(`${current}: console.error — ${message.text()}`);
      }
    });
    page.on('pageerror', (error) => {
      problems.push(`${current}: pageerror — ${error.message}`);
    });
    // A failed request is the shape the config.js bug took: the console error text is only
    // "Failed to load resource", which names nothing. Capturing the URL is what makes a failure
    // actionable rather than a puzzle.
    page.on('requestfailed', (request) => {
      problems.push(`${current}: request failed — ${request.url()}`);
    });
    page.on('response', (response) => {
      if (response.status() >= 400) {
        problems.push(`${current}: HTTP ${response.status()} — ${response.url()}`);
      }
    });

    for (const screen of compassScreens(ids)) {
      current = screen.name;
      await page.goto(screen.path, { timeout: 15_000, waitUntil: 'networkidle' });
    }

    expect(
      problems,
      'Every Compass screen must load without a console error, a page error, a failed request or a ' +
        '4xx/5xx response. An error present on every screen is what hides the next real one.',
    ).toEqual([]);
  });
});
