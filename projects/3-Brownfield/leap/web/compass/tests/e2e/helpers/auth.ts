/**
 * Minimal stub-login helper for Compass's OWN e2e suite (`web/compass/tests/e2e/`, its own
 * `playwright.config.ts` and its own e2e-stack reset -- deliberately not shared with
 * `web/timesheet/tests/e2e/helpers/auth-session.ts`, which is scoped to that suite's identity
 * isolation rules). Drives the same Development-only `/auth/stub-login` real sign-in pipeline
 * (`AuthEndpoints.cs` `HandleStubLogin` -> `SignInService.CompleteSignInAsync`) that
 * `auth-session.ts` documents -- no JWTs, no `localStorage` token, a real HttpOnly session cookie.
 */
import { type APIRequestContext, expect } from '@playwright/test';

export const API_BASE = 'http://localhost:5009';

/**
 * Establishes a real cookie session for an `@example.test` identity (matches this stack's
 * `GoogleAuth__AllowedDomain=example.test`, playwright.config.ts's webServer). Passing `request`
 * as `page.request` puts the resulting session cookie in the browser context's cookie jar --
 * cookie matching ignores port and matches on host alone, so it is sent on subsequent navigations
 * to the Compass dev server (`localhost:5176`) even though stub-login itself runs against the API
 * (`localhost:5009`). `maxRedirects: 0` observes the sign-in redirect (and its `Set-Cookie`)
 * directly rather than following it through to the SPA shell.
 */
export async function stubLogin(
  request: APIRequestContext,
  opts: { email: string; groups?: string },
): Promise<void> {
  const params = new URLSearchParams({ email: opts.email });
  if (opts.groups) {
    params.set('groups', opts.groups);
  }
  const resp = await request.get(`${API_BASE}/auth/stub-login?${params.toString()}`, {
    maxRedirects: 0,
  });
  expect(
    [302, 303],
    `stub-login for ${opts.email} should redirect after minting the session cookie (got ${resp.status()})`,
  ).toContain(resp.status());
}
