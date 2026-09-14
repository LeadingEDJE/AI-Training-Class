// FR-006 / FR-008 (issue #217): a bare, unauthenticated visit to a deep Compass route must reach
// sign-in, not a static "not authorised" page rendered inside the SPA.
//
// **This suite proves the CLIENT-SIDE half only** — that `installSessionExpiredRedirect`
// (src/lib/session-redirect.ts) fires and sends the browser to `/auth/login?returnUrl=<the exact
// requested location>`. It does not complete the round trip: this stack registers no SAML scheme, so
// `/auth/login` answers the documented 503 degraded state (`AuthEndpoints.HandleLogin`). The
// backend's half — authenticating and landing back on the requested location — is proven under the
// real SAML mock stack by web/timesheet/tests/e2e/saml/compass-standalone.spec.ts. Neither suite
// covers FR-008 alone.
//
// Also proves `web/compass/vite.config.ts` proxies `/auth` to the API: without that, `/auth/login`
// falls through to the Compass SPA fallback and this suite would watch the shell reload instead.

import { test, expect } from '@playwright/test';

/**
 * Visits a Compass URL and asserts the client-side redirect landed on `/auth/login` with the expected
 * `returnUrl`, WITHOUT requiring that destination to load.
 *
 * **Polls `page.url()` rather than awaiting `waitForURL`, and the engines are why.** `/auth/login`
 * answers 503 here, which through the vite proxy surfaces as a network-level failure. Chromium and
 * WebKit render an error page and fire `load`; Firefox raises `NS_ERROR_NET_ERROR_RESPONSE` and
 * `page.goto` never resolves. The load event was never the subject — the URL and its `returnUrl` are.
 *
 * The real navigation is deliberate: stubbing the destination would quietly drop the proxy coverage
 * the header describes.
 */
async function expectRedirectToSignIn(
  page: Parameters<Parameters<typeof test>[1]>[0]['page'],
  from: string,
  expectedReturnUrl: string,
) {
  // `domcontentloaded`, so the SPA is running and `installSessionExpiredRedirect` can fire — but we do
  // not wait for the redirect DESTINATION, which is the part that errors.
  await page.goto(from, { waitUntil: 'domcontentloaded' }).catch(() => undefined);

  await expect
    .poll(() => page.url(), { timeout: 15_000 })
    .toContain(`/auth/login?returnUrl=${expectedReturnUrl}`);
}

test.describe('Compass unauthenticated deep-link redirect [critical]', () => {
  test('a bare visit to a deep Compass route with no session redirects to sign-in, preserving the requested location (FR-006, FR-008)', async ({
    page,
  }) => {
    await expectRedirectToSignIn(page, '/compass/team-directory', '%2Fcompass%2Fteam-directory');

    // Reaching sign-in means reaching none of Compass (FR-006) -- the nav landmark RootLayout
    // renders on every Compass route must not still be present once the redirect has fired.
    await expect(page.getByRole('navigation', { name: 'Compass navigation' })).not.toBeVisible();
  });

  test('a bare visit to the Compass root also redirects, not just deep sub-routes', async ({
    page,
  }) => {
    await expectRedirectToSignIn(page, '/compass/', '%2Fcompass%2F');
  });

  test('a query string on the requested location is preserved in returnUrl', async ({ page }) => {
    await expectRedirectToSignIn(
      page,
      '/compass/assignments?clientId=42',
      '%2Fcompass%2Fassignments%3FclientId%3D42',
    );
  });
});
