import { useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { AvailabilityReportData } from './types';

export function useAvailabilityReport() {
  return useQuery({
    queryKey: ['compass', 'reports', 'availability'],
    queryFn: () => readGated<AvailabilityReportData>('/api/compass/reports/availability'),
  });
}
