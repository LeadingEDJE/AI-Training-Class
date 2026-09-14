// The mobile nav disclosure (owner request, 2026-08-25). Below `md` `CompassNav` collapses its links
// behind a Menu button — a recorded departure, since the design source is silent on mobile navigation
// (see web/compass/README.md § "Recorded departure: the mobile nav is a disclosure").
//
// **Why this spec exists separately from the unit tests, which already cover the behaviour.** The
// panel is only in the DOM while it is OPEN, and nothing else opens it: `wcag-sweep.critical.spec.ts`
// loads each screen and scans what it finds, so the panel's own markup — its links, its contrast, its
// aria wiring — is invisible to every a11y gate in the repository. New DOM that no gate can see is the
// shape this whole area keeps getting caught by (see the `helpers/axe.ts` exclusion history in
// specs/011's evidence artifact). So the panel gets scanned here, with it open.
import { expect, test } from '@playwright/test';

import { stubLogin } from './helpers/auth';
import { expectNoWcagViolationsAt } from './helpers/axe';

/** Below `md`. 390 is the narrowest viewport `docs/nfr/NFR-catalog.md` P4 names. */
const NARROW = { width: 390, height: 844 } as const;
/** At `md` exactly — the bar must be back. 767/768 is the pair feature 011 found a defect straddling. */
const AT_MD = { width: 768, height: 1024 } as const;

test.describe('@compass the mobile nav disclosure [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await stubLogin(page.request, {
      email: 'nav.disclosure@example.test',
      groups: 'Compass-SuperAdmin-dev',
    });
  });

  test('collapses the links behind a Menu button below md, and opens to reveal them', async ({
    page,
  }) => {
    await page.setViewportSize(NARROW);
    await page.goto('/compass/', { timeout: 15_000 });

    const button = page.getByRole('button', { name: 'Menu' });
    await expect(button).toBeVisible({ timeout: 15_000 });
    await expect(button).toHaveAttribute('aria-expanded', 'false');

    // Not merely hidden — absent. There is nothing for a screen reader to reach past the button.
    await expect(page.getByRole('link', { name: 'Team Directory' })).toHaveCount(0);

    await button.click();
    await expect(button).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('link', { name: 'Team Directory' })).toBeVisible();
  });

  test('the OPEN panel is WCAG 2.1 AA clean — the state no other gate ever scans', async ({
    page,
  }) => {
    await page.setViewportSize(NARROW);
    await page.goto('/compass/', { timeout: 15_000 });
    await page.getByRole('button', { name: 'Menu' }).click();
    await expect(page.getByRole('link', { name: 'Team Directory' })).toBeVisible();

    // Zero exclusions, same as every other sweep in this tree.
    await expectNoWcagViolationsAt(page, 'compass-index (nav panel open)', NARROW);
  });

  test('Escape closes the panel and returns focus to the button', async ({ page }) => {
    await page.setViewportSize(NARROW);
    await page.goto('/compass/', { timeout: 15_000 });

    const button = page.getByRole('button', { name: 'Menu' });
    await button.click();
    await expect(button).toHaveAttribute('aria-expanded', 'true');

    await page.keyboard.press('Escape');

    await expect(button).toHaveAttribute('aria-expanded', 'false');
    // Without this the panel dismisses to `<body>` and a keyboard user restarts the tab order at the
    // top of the document. Asserted in a real browser because jsdom's focus model is not the browser's.
    await expect(button).toBeFocused();
  });

  test('the button is reachable and operable by keyboard alone', async ({ page }) => {
    await page.setViewportSize(NARROW);
    await page.goto('/compass/', { timeout: 15_000 });

    const button = page.getByRole('button', { name: 'Menu' });
    await button.focus();
    await expect(button).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(button).toHaveAttribute('aria-expanded', 'true');
    // `exact` because the accessible-name match is a SUBSTRING by default, and this page also
    // renders team-directory links named after EDJErs -- one of Feature 018's seeded OOTO E2E
    // personas once had a surname whose link text also matched 'Reports', causing a strict-mode
    // violation. The nav label is exactly 'Reports', so asserting that is both narrower and closer
    // to what this test is about, and robust to future persona-name changes.
    await expect(page.getByRole('link', { name: 'Reports', exact: true })).toBeVisible();
  });

  test('at md the bar is back and there is no Menu button', async ({ page }) => {
    await page.setViewportSize(AT_MD);
    await page.goto('/compass/', { timeout: 15_000 });

    await expect(page.getByRole('link', { name: 'Team Directory' })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByRole('button', { name: 'Menu' })).toHaveCount(0);
  });
});
