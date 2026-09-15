import { useState } from 'react';
import { SalesDashboardPage } from './SalesDashboardPage';
import { useDashboardBreakdown, useSalesDashboard } from './useSalesDashboard';
import type { DashboardCategory } from './types';

const DEFAULT_CATEGORY: DashboardCategory = 'expiring-sows';

/** Connects the Sales Dashboard screen to the read surface. */
export function SalesDashboardRoute() {
  const [selectedCategory, setSelectedCategory] = useState<DashboardCategory>(DEFAULT_CATEGORY);

  const dashboardQuery = useSalesDashboard();
  const breakdownQuery = useDashboardBreakdown(selectedCategory);

  return (
    <SalesDashboardPage
      dashboard={dashboardQuery.data}
      isDashboardPending={dashboardQuery.isPending}
      // `kind === 'failed'` is the only source that matters in practice: a 403 REFUSAL and a 500
      // failure both resolve into it, so `isError` here is mostly redundant belt-and-suspenders.
      isDashboardError={dashboardQuery.isError || dashboardQuery.data?.kind === 'failed'}
      selectedCategory={selectedCategory}
      onSelectCategory={setSelectedCategory}
      breakdown={breakdownQuery.data}
      isBreakdownPending={breakdownQuery.isPending}
      isBreakdownError={breakdownQuery.isError || breakdownQuery.data?.kind === 'failed'}
    />
  );
}
