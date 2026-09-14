/**
 * Drives re-login for the Compass SPA (FR-006/FR-008, spec 002-compass-platform-foundations).
 *
 * `apiFetch` (`./api-url.ts`) already dispatches a one-shot `session-expired` window event on any
 * 401, but until now nothing listened for it — an unauthenticated visit to a deep Compass route
 * (e.g. `/compass/team-directory`) rendered a static "not authorised" state instead of reaching
 * sign-in at all. The LEAP shell (`web/shell/shell.js`'s `runAuthGate`) and OOTO
 * (`web/ooto/src/utils/user.ts`'s `redirectLogin`) both already redirect their own SPA to
 * `/auth/login` on an unauthenticated `/api/me`; this is the same pattern for Compass, with the
 * current path + query preserved as `returnUrl` so the backend's already-proven `SafeReturnUrl`
 * mechanism can send the user back to the exact location they requested.
 */

/** The subset of `Location` this module reads — narrowed for easy test doubles. */
export type RedirectSource = Pick<Location, 'pathname' | 'search'>;

/** Builds the `/auth/login` target that returns the user to `location` after authenticating. */
export function buildLoginRedirectUrl(location: RedirectSource): string {
  const requested = `${location.pathname}${location.search}`;
  return `/auth/login?returnUrl=${encodeURIComponent(requested)}`;
}

/**
 * Wires the `session-expired` event to a full-page redirect to sign-in. Call once at startup
 * (`main.tsx`) — the underlying event is already one-shot per session, so this listener does not
 * need to guard against firing more than once.
 */
export function installSessionExpiredRedirect(): void {
  window.addEventListener('session-expired', () => {
    window.location.assign(buildLoginRedirectUrl(window.location));
  });
}
