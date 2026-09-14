// Positive control: proves the Compass Playwright setup discovers and runs a spec at all, and that
// the dev server serves both the root path and an arbitrary sub-path (Vite's SPA fallback), before
// any spec here depends on either.
//
// Sign-in first, or nothing else works: `installSessionExpiredRedirect` (src/lib/session-redirect.ts)
// sends an unauthenticated visit to `/auth/login` before the SPA renders, so every `page.goto` below
// would land on the redirect target instead of the Compass shell. tests/e2e/auth.critical.spec.ts
// covers that redirect itself.
//
// Each test signs in as its OWN identity, never a shared email: these run in parallel workers, and
// two concurrent stub-logins for the same identity invalidate each other's session cookie.

import { test, expect } from '@playwright/test';
import { stubLogin } from './helpers/auth';

test.describe('Compass smoke tests [critical]', () => {
  test('/compass/ loads on the Team Directory', async ({ page }) => {
    await stubLogin(page.request, { email: 'compass.smoke.loads@example.test' });

    await page.goto('/compass/');
    await expect(page).toHaveTitle('EDJE Compass');
    await expect(page.getByRole('heading', { name: 'Team Directory' })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByRole('navigation', { name: 'Compass navigation' })).toBeVisible();
  });

  test('a deep link resolves', async ({ page }) => {
    // The subject is the dev server's SPA fallback: it must serve index.html for an arbitrary
    // sub-path instead of a raw 404, so the shell boots — proved by the nav landmark, which the root
    // layout renders on every route. The specimen path must stay genuinely unmatched; if a real
    // route is ever added at it, this test quietly starts proving something else.
    await stubLogin(page.request, { email: 'compass.smoke.deeplink@example.test' });

    await page.goto('/compass/not-a-real-page');
    await expect(page.getByRole('navigation', { name: 'Compass navigation' })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByRole('heading', { name: 'Page Not Found' })).toBeVisible();
  });
});
