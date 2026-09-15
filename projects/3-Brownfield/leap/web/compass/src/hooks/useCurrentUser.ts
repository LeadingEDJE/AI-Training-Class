import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../lib/api-url';

export interface ImpersonatorInfo {
  edjeId: string;
  displayName: string;
  email: string;
}

/** The signed-in identity, decoded from the session JWT on the client. */
export interface CurrentUser {
  edjeId: string;
  email: string;
  displayName: string;
  privileges: string[];
  impersonator: ImpersonatorInfo | null;
}

export const currentUserQueryKey = ['current-user'] as const;

/** Reads the signed-in identity from `GET /api/me`. A 401 is surfaced as a generic error through `isError`. */
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
