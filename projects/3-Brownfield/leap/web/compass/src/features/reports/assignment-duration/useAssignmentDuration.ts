import { useQuery } from '@tanstack/react-query';
import { readGated, type ReportLoad } from '../../../lib/report-load';
import type { AssignmentDurationRow } from './types';

/** Reads the Client Assignment Duration report (AC-39, FR-014). */
export function useAssignmentDuration() {
  return useQuery<ReportLoad<AssignmentDurationRow[]>>({
    queryKey: ['compass', 'reports', 'assignment-duration'],
    queryFn: () => readGated<AssignmentDurationRow[]>('/api/compass/reports/assignment-duration'),
  });
}
