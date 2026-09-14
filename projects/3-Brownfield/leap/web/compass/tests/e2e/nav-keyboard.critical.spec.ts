// Keyboard access to the DESKTOP navigation bar — the half `nav-disclosure.critical.spec.ts` does not
// cover (it owns the below-`md` Menu button and its panel).
//
// **Ported from `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts`, which this replaces
// (`docs/TEST-STRATEGY.md` Rule 2).** That file scanned Compass through the timesheet Vite proxy on
// `:5173` — an origin Compass never serves from — and every axe assertion in it duplicated
// `wcag-sweep.critical.spec.ts` at fewer viewports. Its ONE uniquely-covered fact was this: that a
// keyboard user can reach each nav link and SEE where they are. axe cannot check that (a focus style
// is not a violation of anything), and jsdom cannot either, because the indicator comes from a
// compiled Tailwind `focus-visible:` class and jsdom has no cascade to compute it from. So it moves
// rather than being deleted.
import { expect, test } from '@playwright/test';

import { stubLogin } from './helpers/auth';

/** Above `md`, where the links are a bar rather than a disclosure panel. */
const DESKTOP = { width: 1280, height: 800 } as const;

test.describe('@compass desktop nav keyboard access [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await stubLogin(page.request, {
      email: 'nav.keyboard@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });
    await page.setViewportSize(DESKTOP);
    await page.goto('/compass/', { timeout: 15_000 });
  });

  /**
   * Every link, focused directly rather than tabbed to.
   *
   * **`.focus()`, not `Tab`, because this test is about the STYLE and the one below is about the
   * ORDER.** Split deliberately: driving focus directly means a failure here names a missing focus
   * indicator and nothing else, while a traversal regression fails the next test instead. The
   * original was a single test doing both, so either cause produced the same message.
   */
  test('every nav link shows a visible focus indicator when focused', async ({ page }) => {
    const nav = page.getByRole('navigation', { name: 'Compass navigation' });
    await expect(nav).toBeVisible({ timeout: 15_000 });
    // The nav renders unconditionally, EMPTY, before the async `/api/me` fetch resolves — reading its
    // links right after `toBeVisible()` observed zero of them. Wait for the first real one.
    await expect(nav.getByRole('link').first()).toBeVisible({ timeout: 15_000 });

    const links = await nav.getByRole('link').all();

    // A loop over zero links would assert nothing and PASS — the fail-open shape that has cost this
    // repo eight gates before. Asserted before the loop, not after.
    expect(links.length, 'the nav must render links for this to measure anything').toBeGreaterThan(
      0,
    );

    for (const link of links) {
      const label = (await link.textContent())?.trim() ?? '(unnamed)';
      await link.focus();
      await expect(link, `"${label}" did not take focus`).toBeFocused();

      const indicator = await link.evaluate((element) => {
        const style = getComputedStyle(element);
        return { outlineStyle: style.outlineStyle, boxShadow: style.boxShadow };
      });

      expect(
        indicator.outlineStyle !== 'none' || indicator.boxShadow !== 'none',
        `"${label}" has no visible focus indicator (outline: ${indicator.outlineStyle}, ` +
          `box-shadow: ${indicator.boxShadow}). A keyboard user cannot see where they are.`,
      ).toBe(true);
    }
  });

  /**
   * Traversal order — and on WebKit specifically, this is a regression guard for a bug that shipped.
   *
   * **Runs on every engine, and must keep doing so.** WebKit does not put plain `<a href>` in the Tab
   * cycle unless the OS-level "Full Keyboard Access" preference is on, which it is not for Playwright's
   * bundled build. `CompassNav`'s links were plain anchors, so on WebKit they were unreachable by
   * keyboard — a real defect, always reproducible, that hid for weeks only because a pull request runs
   * chromium alone. It was fixed by the explicit
   * `tabIndex={0}` on `NavLink`, and this test is what fails if that attribute is ever removed.
   *
   * So do not skip this on WebKit. WebKit is the engine it exists for.
   */
  test('Tab walks the nav links in their rendered order', async ({ page }) => {
    const nav = page.getByRole('navigation', { name: 'Compass navigation' });
    await expect(nav).toBeVisible({ timeout: 15_000 });
    await expect(nav.getByRole('link').first()).toBeVisible({ timeout: 15_000 });

    const expectedLabels = (await nav.getByRole('link').allTextContents()).map((text) =>
      text.trim(),
    );

    expect(
      expectedLabels.length,
      'the nav must render links for this to measure anything',
    ).toBeGreaterThan(0);

    // A freshly-loaded page starts with nothing focused, so the first Tab reaches the first tabbable
    // element — no prior click, which would risk landing on a link by accident.
    for (const label of expectedLabels) {
      await page.keyboard.press('Tab');
      await expect(
        page.locator(':focus'),
        `Tab did not reach "${label}" in the order the nav renders it`,
      ).toHaveText(label);
    }
  });
});
