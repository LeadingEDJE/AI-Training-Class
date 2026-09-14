import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../lib/api-url';

/** Provenance block naming the SuperAdmin behind an active impersonation session. */
export interface ImpersonatorInfo {
  edjeId: string;
  displayName: string;
  email: string;
}

/**
 * The signed-in identity as reported by the server session probe `GET /api/me`.
 * This is the single source of truth for identity and privileges in the SPA —
 * no JWT is decoded client-side. `impersonator` is non-null only while the
 * session is impersonating another user.
 */
export interface CurrentUser {
  edjeId: string;
  email: string;
  displayName: string;
  privileges: string[];
  impersonator: ImpersonatorInfo | null;
}

/** Stable React Query key for the current-user probe; use it to invalidate after impersonation. */
export const currentUserQueryKey = ['current-user'] as const;

/**
 * Reads the signed-in identity from `GET /api/me` via TanStack Query.
 *
 * A 401 means "no active session" and resolves to `null` (not an error) — the
 * `session-expired` event, dispatched by {@link apiFetch} and wired to a redirect by
 * `installSessionExpiredRedirect` (`../lib/session-redirect`), drives re-login.
 * Any other non-2xx status is a genuine error surfaced through `isError`.
 */
export function useCurrentUser(): UseQueryResult<CurrentUser | null> {
  return useQuery<CurrentUser | null>({
    queryKey: currentUserQueryKey,
    queryFn: async () => {
      const response = await apiFetch(apiUrl('/api/me'));
      if (response.status === 401) {
        return null;
      }
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      return (await response.json()) as CurrentUser;
    },
  });
}
