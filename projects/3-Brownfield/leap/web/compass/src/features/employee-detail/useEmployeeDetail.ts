import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { EmployeeDetail } from './types';

/**
 * Reads one EDJEr's detail (AC-8, AC-10, AC-11).
 *
 * A 404 resolves to `null` rather than throwing. That is not error-swallowing: the server
 * answers 404 both for a record that does not exist and for one this viewer is not entitled to see
 * (FR-021), deliberately, so a sequential id cannot be used to probe for existence. Treating it as an
 * error would render "something went wrong" for what is a legitimate, expected answer.
 */
export function useEmployeeDetail(employeeId: number) {
  return useQuery({
    queryKey: ['compass', 'employee-detail', employeeId],
    // `null`, not `undefined`: React Query rejects an undefined query result outright and surfaces
    // it as an error, which would turn the deliberate 404 below back into "something went wrong".
    queryFn: async (): Promise<EmployeeDetail | null> => {
      const response = await apiFetch(apiUrl(`/api/compass/team-directory/${employeeId}`));

      if (response.status === 404) {
        return null;
      }

      if (!response.ok) {
        throw new Error(`Employee detail request failed with status ${response.status}`);
      }

      return (await response.json()) as EmployeeDetail;
    },
  });
}
