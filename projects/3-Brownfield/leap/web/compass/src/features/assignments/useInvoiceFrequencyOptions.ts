import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { InvoiceFrequencyOption } from './AssignmentForm';

/** The assignment-scoped cadence read (US6, #64). See {@link useInvoiceFrequencyOptions}. */
const CADENCES_URL = '/api/compass/assignments/pickers/invoice-frequency-types';

/**
 * The selectable invoice-frequency cadences an assignment may override its client's default with
 * (feature 006 US6, issue #64).
 *
 * **Why this reads the assignment-scoped route and NOT `fetchLookups`.** `fetchLookups` is rooted at
 * `/api/compass/v1/admin`, which is gated by `RolePolicy.CompassSuperAdmin` because it is the
 * CONFIGURATION surface — it creates and updates cadences. Setting an assignment's override needs
 * only `CompassOps`, so pointing this hook at the admin route made a Compass Ops user authorized to
 * choose a cadence but forbidden from listing the cadences to choose from. The 403 degraded to an
 * empty list, so the selector silently offered "Use the client default" alone and looked like a
 * client with no cadences configured rather than a permissions fault. Do not route this back through
 * `fetchLookups` to save a few lines — the admin gate is correct for what it guards.
 *
 * **Why this lives in a hook the ROUTE containers call.** `AssignmentForm` and both pages are
 * rendered bare by their unit tests — no `QueryClientProvider` anywhere in those suites — so they are
 * contractually dependency-free and take their data as props. A `useQuery` in any of them turns every
 * one of those tests into `No QueryClient set`.
 *
 * `activeOnly` is true: a retired cadence must not be offered as a NEW choice. An assignment already
 * carrying a since-retired one keeps it — `AssignmentForm` re-adds that single value to its own
 * option list so editing an unrelated field cannot silently clear it (grandfathering, T117).
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
