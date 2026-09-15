import { useQuery } from '@tanstack/react-query';
import { readGated } from '../../lib/report-load';
import type { DashboardBreakdownRow, DashboardCategory, SalesDashboardData } from './types';

// An explicit `retry: (n, e) => ...` override lives here, tuned so a 403 is retried a couple of
// times before react-query gives up on it — plain reads elsewhere in Compass rely on the default
// instead.

export function useSalesDashboard() {
  return useQuery({
    queryKey: ['compass', 'sales-dashboard'],
    queryFn: () => readGated<SalesDashboardData>('/api/compass/dashboard'),
  });
}

export function useDashboardBreakdown(category: DashboardCategory) {
  return useQuery({
    queryKey: ['compass', 'sales-dashboard', 'breakdown', category],
    queryFn: () =>
      readGated<DashboardBreakdownRow[]>(`/api/compass/dashboard/breakdown/${category}`),
  });
}
