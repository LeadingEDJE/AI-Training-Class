export type RedirectSource = Pick<Location, 'pathname' | 'search'>;

/** Builds the `/auth/login` target, discarding the current path since SafeReturnUrl handles that server-side. */
export function buildLoginRedirectUrl(location: RedirectSource): string {
  const requested = `${location.pathname}${location.search}`;
  return `/auth/login?returnUrl=${encodeURIComponent(requested)}`;
}

export function installSessionExpiredRedirect(): void {
  window.addEventListener('session-expired', () => {
    window.location.assign(buildLoginRedirectUrl(window.location));
  });
}
