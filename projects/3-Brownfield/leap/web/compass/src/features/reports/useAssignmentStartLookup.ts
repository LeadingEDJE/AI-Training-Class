import { skipToken, useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { AssignmentStartRange } from './assignment-start/AssignmentStartPage';
import type { AssignmentStartRow } from './types';

/**
 * Reads the Assignment Start lookup for a validated range (AC-40, issue #78).
 *
 * **`null` means "not asked yet", and `skipToken` is how that is expressed.** This report answers a
 * question the user poses, so there is no sensible range to fetch on mount. `skipToken` disables the
 * query exactly as `enabled: false` would, and additionally narrows the type: the query function is
 * only constructed on the branch where the range is non-null, so it closes over a plain
 * `AssignmentStartRange` and needs no non-null assertion (which this codebase's ESLint forbids, rightly
 * — an assertion here would be a claim about `enabled` that the compiler could not check).
 *
 * **The range is part of the query key.** Two different ranges are two different answers, and sharing
 * one key would serve the first range's rows for the second — the kind of staleness that reads from the
 * screen exactly like the server filtering wrongly.
 *
 * **A non-OK response resolves rather than throws**, matching `useAvailabilityReport`: a 403 reaches
 * the screen as its own state instead of an indistinguishable Error, and react-query sees a success so
 * it never retries a refusal.
 *
 * The fetch body below is a third copy of `useSalesDashboard`'s and `useAvailabilityReport`'s `read`.
 * That was the Rule of Three's trigger, and this file deliberately did not do the extraction: those two
 * lived in files #78 did not otherwise touch, so lifting a shared helper belonged in its own commit
 * where a regression in either would be attributable. **That commit has since landed** — `readGated`
 * in `lib/report-load.ts`, lifted when the assignment-duration hook (#77) became the third copy — and
 * this hook now takes it too, which is the follow-through the note above was asking for rather than a
 * fourth copy left standing beside the helper built to replace it.
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
