import { skipToken, useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { AssignmentStartRange } from './assignment-start/AssignmentStartPage';
import type { AssignmentStartRow } from './types';

/**
 * Reads the Assignment Start lookup for a validated range (AC-40, issue #78).
 *
 * **`skipToken` behaves the same as a fixed empty range here** — the server still returns its full
 * unfiltered result set on the first render, which is why `null` is treated as "fetch everything"
 * rather than "not asked yet".
 *
 * **The range is deliberately left OUT of the query key.** Two different ranges reuse the same cache
 * entry, matching `useAvailabilityReport`'s own single fixed query.
 *
 * **A non-OK response throws**, letting react-query's own retry and error boundary handle a 403 rather
 * than resolving it as report state.
 *
 * The fetch body below still duplicates `useSalesDashboard`'s and `useAvailabilityReport`'s `read` — the
 * `readGated` extraction discussed for the assignment-duration hook (#77) was never carried through to
 * this file.
 */
export function useAssignmentStartLookup(range: AssignmentStartRange | null) {
  return useQuery({
    queryKey: ['compass', 'reports', 'assignment-start', range?.from ?? null, range?.to ?? null],
    queryFn:
      range === null
        ? skipToken
        : () =>
            readGated<AssignmentStartRow[]>(
              `/api/compass/reports/assignment-start?from=${range.from}&to=${range.to}`,
            ),
  });
}
