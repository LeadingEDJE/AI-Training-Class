import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { ClientDirectoryRow } from './types';

/** The AC-12 controls sent to the server. */
export interface ClientDirectoryParams {
  search?: string;
  sort?: string;
  desc?: boolean;
}

/** Builds the query string, omitting blank values so absent and empty mean the same thing. */
export function buildClientDirectoryQuery(params: ClientDirectoryParams): string {
  const query = new URLSearchParams();

  if (params.search?.trim()) query.set('search', params.search.trim());
  if (params.sort?.trim()) query.set('sort', params.sort.trim());
  if (params.desc) query.set('desc', 'true');

  const serialised = query.toString();
  return serialised ? `?${serialised}` : '';
}

/** Reads the Client Directory (AC-12). */
export function useClientDirectory(params: ClientDirectoryParams) {
  return useQuery({
    // Keep the previous rows visible while a new search or sort is in flight, so the table does not
    // blank and reflow on every keystroke.
    placeholderData: (previous) => previous,
    queryKey: ['compass', 'client-directory', params],
    queryFn: async (): Promise<ClientDirectoryRow[]> => {
      const response = await apiFetch(
        apiUrl(`/api/compass/client-directory${buildClientDirectoryQuery(params)}`),
      );

      if (!response.ok) {
        throw new Error(`Client directory request failed with status ${response.status}`);
      }

      return (await response.json()) as ClientDirectoryRow[];
    },
  });
}
