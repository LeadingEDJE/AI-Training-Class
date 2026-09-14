/**
 * Shared axe-core sweep for the Compass e2e suite (AC-NFR-5, AC-NFR-7; issue #84).
 *
 * **Extracted at the fifth copy, not the second.** `sales-dashboard`, `reports-availability`,
 * `reports-assignment-duration` and `reports-assignment-start` each carry their own transcription of
 * this block; the WCAG sweep would have been the fifth. The Rule of Three triggered two copies ago.
 *
 * **Why the script is injected by path rather than imported.** `web/compass` is an ESM package
 * ("type": "module"), so there is no ambient `require` to resolve axe-core's bundled script by package
 * path — `createRequire` supplies one. `@axe-core/playwright` is deliberately NOT used: feature 007
 * Phase 1 forbids a new package, and it is a `web/timesheet` devDependency only.
 */
import { createRequire } from 'node:module';
import { expect, type Page } from '@playwright/test';
import type Axe from 'axe-core';

/** axe-core's bundled browser script, resolved from this package's own dependency. */
export const axeScriptPath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');

/** The five viewport sizes AC-NFR-7 names. */
export const VIEWPORTS = [
  { width: 390, height: 844 },
  { width: 767, height: 1024 },
  { width: 768, height: 1024 },
  { width: 1280, height: 800 },
  { width: 1920, height: 1080 },
] as const;

/**
 * **There are no exclusions, and adding one is a change of policy — not a workaround.**
 *
 * This used to exclude `.overflow-x-auto`, the shared `Table` primitive's scroll wrapper, because it had
 * no `tabindex` and so was not keyboard reachable. Issue #84 diagnosed that correctly and excluded it
 * *with the reason written down* — which was honest, and still left the defect shipping and untracked.
 *
 * Feature 011 (issue #83) fixed the defect: `ScrollableRegion` gives the wrapper a focus target and an
 * accessible name, so the exclusion is no longer needed. **Deleting it was the highest-leverage change
 * in that feature**, because #84's sweep already walks 23 screens at five viewports — the exclusion was
 * the one thing keeping that whole walk blind. Measured before removal: `axe` reported `NONE` on all ten
 * screens checked at 390px, including `reports/availability`, which carries three `serious`
 * `scrollable-region-focusable` nodes without it.
 *
 * Kept as an empty array rather than deleted outright so the `exclude` contract at the call sites stays
 * one shape, and so `axe-no-exclusions.test.ts` has something to assert against. If you are about to put
 * a selector in here: anything axe reports is a finding to fix. The lesson from #84 is not "exclude with
 * a comment", it is "a documented exclusion with no tracking issue is a deferred defect with no owner".
 */
export const SHARED_CHROME_EXCLUSIONS: string[][] = [];

/**
 * Runs axe against the current page at every {@link VIEWPORTS} size and asserts zero violations.
 *
 * Injects the script once and then resizes, rather than reloading per viewport: the page under test is
 * already in the state the caller wants scanned, and re-navigating would discard it.
 *
 * @param page The page, already navigated AND settled — scan a loading state and the result is noise.
 * @param screenName Named in the failure message, because a sweep failure otherwise says only which
 * viewport broke and leaves the reader to work out which of two dozen screens it was.
 */
export async function expectNoWcagViolations(page: Page, screenName: string): Promise<void> {
  await page.addScriptTag({ path: axeScriptPath });

  for (const viewport of VIEWPORTS) {
    await page.setViewportSize(viewport);

    const results = await page.evaluate(async (exclude) => {
      const axe = (window as unknown as { axe: typeof Axe }).axe;
      return axe.run({ exclude }, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] });
    }, SHARED_CHROME_EXCLUSIONS);

    expect(
      results.violations,
      `${screenName} at ${viewport.width}x${viewport.height}: ` +
        JSON.stringify(results.violations, null, 2),
    ).toHaveLength(0);
  }
}

/**
 * Runs axe at ONE named viewport and asserts zero violations (feature 011, T011).
 *
 * **Why this exists next to {@link expectNoWcagViolations} rather than replacing it.** That function
 * loops all five {@link VIEWPORTS} and reports whichever one broke, which is right for a sweep covering
 * two dozen screens. Feature 011 needs the opposite shape: assert a specific screen at a specific width
 * and say so, because its findings are viewport-specific — `reports/availability` carries three
 * `scrollable-region-focusable` nodes at 390px and one at 768px, and a message naming "one viewport of
 * five" loses that distinction.
 *
 * Deliberately reuses {@link VIEWPORTS}' members rather than accepting arbitrary sizes: the five widths
 * are the NFR-catalog standard, and a second list of widths is a second thing to keep in step.
 *
 * ⚠️ **This inherits {@link SHARED_CHROME_EXCLUSIONS}, so it is blind to `.overflow-x-auto` exactly as
 * the sweep is.** That is intentional for now — one exclusion policy, not two. Feature 011 T017 deletes
 * that constant, and when it does **both** this function and {@link expectNoWcagViolations} must be
 * updated together; leaving either behind keeps half the suite blind.
 *
 * @param page A page already navigated AND settled — scanning a loading state measures the spinner.
 * @param screenName Named in the failure message.
 * @param viewport One of {@link VIEWPORTS}.
 */
export async function expectNoWcagViolationsAt(
  page: Page,
  screenName: string,
  viewport: (typeof VIEWPORTS)[number],
): Promise<void> {
  await page.addScriptTag({ path: axeScriptPath });
  await page.setViewportSize({ width: viewport.width, height: viewport.height });

  const results = await page.evaluate(async (exclude) => {
    const axe = (window as unknown as { axe: typeof Axe }).axe;
    return axe.run({ exclude }, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] });
  }, SHARED_CHROME_EXCLUSIONS);

  expect(
    results.violations,
    `${screenName} at ${viewport.width}x${viewport.height}: ` +
      JSON.stringify(results.violations, null, 2),
  ).toHaveLength(0);
}
