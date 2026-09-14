import { useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { AvailabilityReportData } from './types';

/**
 * Reads the Availability Report's three sections (AC-38).
 *
 * **The local `read` helper is gone — lifted to `lib/report-load.ts` at US3.** This file's previous
 * docstring said so in advance: two occurrences was not enough, and the assignment-duration hook was
 * named as the third that would trigger the extraction (Four Rules of Simple Design). All
 * three now call one `readGated`, so the non-OK-resolves behaviour a 403 depends on has one definition.
 */
export function useAvailabilityReport() {
  return useQuery({
    queryKey: ['compass', 'reports', 'availability'],
    queryFn: () => readGated<AvailabilityReportData>('/api/compass/reports/availability'),
  });
}
