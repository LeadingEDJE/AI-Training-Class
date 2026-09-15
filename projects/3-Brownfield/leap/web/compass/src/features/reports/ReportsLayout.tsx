import { Outlet } from '@tanstack/react-router';
import { PageHeader } from '../../components/ui';
import { TabNav, type TabNavItem } from '../../components/TabNav';

const TABS: TabNavItem[] = [
  { to: '/reports/availability', label: 'Availability Report' },
  { to: '/reports/assignment-duration', label: 'Client Assignment Duration' },
  { to: '/reports/assignment-start', label: 'Assignment Start' },
  { to: '/reports/sow-extension', label: 'SOW Extension Report' },
];

/** The bare reports path — the index route. */
const REPORTS_INDEX = '/reports';

/**
 * What the index route actually renders.
 *
 * `router.ts` redirects `/reports` to `AvailabilityReportRoute`'s own path, so this constant only
 * documents the redirect target for {@link TABS} — the tab strip's active-match logic does not
 * consult it, since the redirect already rewrites the pathname before the strip renders.
 */
const INDEX_RENDERS = '/reports/availability';

/**
 * The reports shell at `/compass/reports` — page header, tab strip, and the active report below.
 *
 * <h3>Tabs are local state, not links</h3>
 * The strip drives which report renders via `useState` (FR-023, Q3), so the four reports share one
 * URL and switching tabs does not add a browser-history entry.
 */
export function ReportsLayout() {
  return (
    <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
      <PageHeader title="Reports" />

      <TabNav label="Reports" items={TABS} indexPath={REPORTS_INDEX} indexRenders={INDEX_RENDERS} />

      <Outlet />
    </main>
  );
}
