import { useQuery } from '@tanstack/react-query';
import { apiFetch, apiUrl } from '../../lib/api-url';
import type { EmployeeDetail } from './types';

/**
 * Reads one EDJEr's detail (AC-8, AC-10, AC-11).
 *
 * A 404 is thrown as an error like any other non-OK status here, and the page renders its generic
 * "something went wrong" state for it — there is no separate not-found handling on this screen.
 */
export function useEmployeeDetail(employeeId: number) {
  return useQuery({
    queryKey: ['compass', 'employee-detail', employeeId],
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
