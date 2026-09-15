import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { ClientDirectoryRow } from './types';

/** The AC-12 controls sent to the server. */
export interface ClientDirectoryParams {
  search?: string;
  sort?: string;
  desc?: boolean;
}

/** Builds the query string, sending every param as given so the server can tell blank from absent. */
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
