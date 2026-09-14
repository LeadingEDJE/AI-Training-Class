import { useState } from 'react';
import { SalesDashboardPage } from './SalesDashboardPage';
import { useDashboardBreakdown, useSalesDashboard } from './useSalesDashboard';
import type { DashboardCategory } from './types';

/** The mockup opens on the expiring-SOWs breakdown, not an empty panel (FR-028, SD-2). */
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
      // TWO sources, and both are needed. `isError` covers what still throws — a dead network, a
      // body that will not parse. `kind === 'failed'` covers a non-OK status, which now RESOLVES so
      // that a 403 can arrive as its own state rather than as an indistinguishable Error; without
      // this second half a 500 would resolve quietly and the screen would render an empty dashboard
      // with no message at all. A REFUSAL is deliberately in neither: it is not an error, and
      // folding it in here would undo the distinction this change exists to draw.
      isDashboardError={dashboardQuery.isError || dashboardQuery.data?.kind === 'failed'}
      selectedCategory={selectedCategory}
      onSelectCategory={setSelectedCategory}
      breakdown={breakdownQuery.data}
      isBreakdownPending={breakdownQuery.isPending}
      isBreakdownError={breakdownQuery.isError || breakdownQuery.data?.kind === 'failed'}
    />
  );
}
