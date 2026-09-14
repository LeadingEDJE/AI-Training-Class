import { useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { DashboardBreakdownRow, DashboardCategory, SalesDashboardData } from './types';

// No `retry` override, and that is the point rather than an omission. Resolving a non-OK status
// instead of throwing already means react-query sees a SUCCESS and never retries it — so a 403 is
// asked exactly once, without a retry policy having to say so. An explicit
// `retry: (n, e) => ...` here would be a second mechanism for a property the shape above already
// guarantees, and dead code besides: nothing would ever call it. What still retries is what should
// — a thrown fetch (dead network, unparseable body), on react-query's own default.

/** Reads the Sales Dashboard's four tile counts (AC-36). */
export function useSalesDashboard() {
  return useQuery({
    queryKey: ['compass', 'sales-dashboard'],
    queryFn: () => readGated<SalesDashboardData>('/api/compass/dashboard'),
  });
}

/** Reads the drill-down rows for one tile (AC-37). */
export function useDashboardBreakdown(category: DashboardCategory) {
  return useQuery({
    queryKey: ['compass', 'sales-dashboard', 'breakdown', category],
    queryFn: () =>
      readGated<DashboardBreakdownRow[]>(`/api/compass/dashboard/breakdown/${category}`),
  });
}
