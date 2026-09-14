declare global {
  interface Window {
    __API_BASE_URL__?: string;
  }
}

/**
 * Prepends the runtime-configured API host prefix (`window.__API_BASE_URL__`) to the given path.
 * The value is same-origin relative by default, so it works unchanged under the `/compass/` mount.
 */
export function apiUrl(path: string): string {
  const base = window.__API_BASE_URL__ || '';
  return `${base}${path}`;
}

let sessionExpired = false;

/**
 * Resets the one-shot `session-expired` guard so the overlay can fire again on the next 401.
 * Intended for use inside test setup only.
 */
export function resetSessionExpired(): void {
  sessionExpired = false;
}

/**
 * Session-aware fetch wrapper for every call to the backend API.
 *
 * Authentication is a server-managed HttpOnly cookie session, so this wrapper injects no credential
 * header — it only ensures the cookie travels via `credentials: 'include'` (a no-op same-origin,
 * required for any cross-origin deployed environment) and surfaces a 401 as a one-shot
 * `session-expired` window event. The one-shot guard matters once there is more than one
 * concurrent `apiFetch` caller (e.g. `CompassNav`'s `/api/me` alongside `App`'s employee fetch) —
 * without it, two 401s in the same render pass would double-fire the event.
 *
 * **Non-negotiable project rule:** never use a bare `fetch()` for an API call. A bare fetch drops
 * the cookie in a cross-origin deployed environment and skips the re-login handling.
 */
export function apiFetch(input: string | URL | Request, init?: RequestInit): Promise<Response> {
  return fetch(input, { credentials: 'include', ...init }).then((response) => {
    if (response.status === 401 && !sessionExpired) {
      sessionExpired = true;
      window.dispatchEvent(new CustomEvent('session-expired'));
    }
    return response;
  });
}
