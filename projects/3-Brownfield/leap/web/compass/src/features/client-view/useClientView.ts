import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { ClientView } from './types';

/**
 * Reads one client's view (AC-13 to AC-16).
 *
 * A 404 resolves to `null` rather than throwing — `null` and not `undefined`, because React Query
 * rejects an undefined result and reports it as an error, which would turn an expected answer into
 * a fault.
 */
export function useClientView(clientId: number) {
  return useQuery({
    queryKey: ['compass', 'client-view', clientId],
    queryFn: async (): Promise<ClientView | null> => {
      const response = await apiFetch(apiUrl(`/api/compass/client-directory/${clientId}`));

      if (response.status === 404) {
        return null;
      }

      if (!response.ok) {
        throw new Error(`Client view request failed with status ${response.status}`);
      }

      return (await response.json()) as ClientView;
    },
  });
}
