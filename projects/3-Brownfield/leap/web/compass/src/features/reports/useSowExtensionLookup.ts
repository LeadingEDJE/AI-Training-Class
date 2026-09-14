import { skipToken, useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { SowExtensionRange } from './sow-extension/SowExtensionPage';
import type { SowExtensionRow } from './types';

/**
 * Reads the SOW Extension Report for a validated range (issue #534) — mirrors
 * `useAssignmentStartLookup`, which this hook is a close sibling of.
 *
 * **`null` means "not asked yet", and `skipToken` is how that is expressed.** This report answers a
 * question the user poses, so there is no sensible range to fetch on mount.
 *
 * **The range is part of the query key.** Two different ranges are two different answers, and sharing
 * one key would serve the first range's rows for the second.
 *
 * **A non-OK response resolves rather than throws**, matching every other Compass report hook: a 403
 * reaches the screen as its own state instead of an indistinguishable Error, and react-query sees a
 * success so it never retries a refusal.
 */
export function useSowExtensionLookup(range: SowExtensionRange | null) {
  return useQuery({
    queryKey: ['compass', 'reports', 'sow-extension', range?.from ?? null, range?.to ?? null],
    queryFn:
      range === null
        ? skipToken
        : () =>
            readGated<SowExtensionRow[]>(
              `/api/compass/reports/sow-extension?from=${range.from}&to=${range.to}`,
            ),
  });
}
