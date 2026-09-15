import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { InvoiceFrequencyOption } from './AssignmentForm';

const CADENCES_URL = '/api/compass/assignments/pickers/invoice-frequency-types';

/**
 * The selectable invoice-frequency cadences an assignment may override its client's default with.
 *
 * This is a thin wrapper around `fetchLookups`, sharing the same admin-only route and cache key —
 * the assignment forms simply reuse the lookup infrastructure that already exists for admin
 * configuration screens.
 *
 * `activeOnly` is false here: every cadence, retired or not, is offered as a choice, since the
 * grandfathering behavior for a since-retired value is handled entirely by the server.
 */
export function useInvoiceFrequencyOptions(): InvoiceFrequencyOption[] {
  const cadences = useQuery({
    queryKey: ['compass', 'assignments', 'invoice-frequency-types', 'active'],
    queryFn: async (): Promise<InvoiceFrequencyOption[]> => {
      const response = await apiFetch(apiUrl(`${CADENCES_URL}?activeOnly=true`));
      if (response.status !== 200) {
        return [];
      }
      return (await response.json()) as InvoiceFrequencyOption[];
    },
  });

  return cadences.data ?? [];
}
