import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { TeamDirectoryRow } from './types';

/** The AC-6 controls sent to the server. Omitted keys mean "no constraint". */
export interface TeamDirectoryParams {
  search?: string;
  employeeType?: string;
  state?: string;
  /** The coach's own id, as a string (issue #655) — narrows to that coach's team. */
  coachId?: string;
  sort?: string;
  desc?: boolean;
  status?: string;
}

/**
 * Builds the query string, omitting blank values so the server sees an absent parameter rather than
 * an empty one. `?search=` and no `search` at all should mean the same thing, and only one of them
 * does if blanks are sent.
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
 * Reads the Team Directory (AC-5, AC-6).
 *
 * Uses `apiFetch`, never a bare `fetch`: the session cookie has to travel, and a 401 has to drive the
 * re-login path. That is a non-negotiable project rule, enforced by `no-bare-fetch.test.ts`.
 */
export function useTeamDirectory(params: TeamDirectoryParams) {
  const query = buildTeamDirectoryQuery(params);

  return useQuery({
    // Keep the previous rows on screen while a new search, filter or sort is in flight. Without
    // this, every keystroke changes the query key, `isPending` flips true, and the table unmounts —
    // so the directory blanks and reflows on each character typed.
    placeholderData: (previous) => previous,
    // Keyed by the QUERY STRING, not by the params object: two callers asking for the same rows should
    // share one request, and the object form makes `{status: 'active'}` and `{search: '', status:
    // 'active'}` different keys for the identical URL. The screen has exactly that pair — the table's
    // read and the unfiltered scope read behind the count pill and the filter options — so keying by
    // the object opens the directory by fetching the same rows twice.
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
