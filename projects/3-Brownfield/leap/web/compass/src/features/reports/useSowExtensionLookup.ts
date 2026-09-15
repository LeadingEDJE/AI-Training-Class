import { skipToken, useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { SowExtensionRange } from './sow-extension/SowExtensionPage';
import type { SowExtensionRow } from './types';

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
