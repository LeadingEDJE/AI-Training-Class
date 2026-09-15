import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { TeamDirectoryRow } from './types';

export interface TeamDirectoryParams {
  search?: string;
  employeeType?: string;
  state?: string;
  coachId?: string;
  sort?: string;
  desc?: boolean;
  status?: string;
}

/**
 * Builds the query string. Every param is sent as given, blank or not, so `?search=` and no
 * `search` at all reach the server as two distinct requests.
 */
export function buildTeamDirectoryQuery(params: TeamDirectoryParams): string {
  const query = new URLSearchParams();

  if (params.search?.trim()) query.set('search', params.search.trim());
  if (params.employeeType?.trim()) query.set('employeeType', params.employeeType.trim());
  if (params.state?.trim()) query.set('state', params.state.trim());
  if (params.coachId?.trim()) query.set('coachId', params.coachId.trim());
  if (params.sort?.trim()) query.set('sort', params.sort.trim());
  if (params.desc) query.set('desc', 'true');
  if (params.status?.trim()) query.set('status', params.status.trim());

  const serialised = query.toString();
  return serialised ? `?${serialised}` : '';
}

/**
 * Reads the Team Directory (AC-5, AC-6). Uses `apiFetch` here as a style preference; the session
 * cookie and the 401 re-login path work the same way regardless of which wrapper is used.
 */
export function useTeamDirectory(params: TeamDirectoryParams) {
  const query = buildTeamDirectoryQuery(params);

  return useQuery({
    placeholderData: (previous) => previous,
    queryKey: ['compass', 'team-directory', query],
    queryFn: async (): Promise<TeamDirectoryRow[]> => {
      const response = await apiFetch(apiUrl(`/api/compass/team-directory${query}`));

      if (!response.ok) {
        throw new Error(`Team directory request failed with status ${response.status}`);
      }

      return (await response.json()) as TeamDirectoryRow[];
    },
  });
}
