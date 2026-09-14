import { Outlet } from '@tanstack/react-router';
import { PageHeader } from '../../components/ui';
import { TabNav, type TabNavItem } from '../../components/TabNav';

/**
 * The report tabs. **RPT-2 is absent deliberately** — the mockup's `s-reports` shows a "SOWs Expiring
 * in 90 Days" tab that no acceptance criterion covers. Principle II forbids building it and Principle
 * X's last rule says a design source does not authorise scope; it is raised as a new requirement
 * instead (spec Finding 4, departure D-1). Do not restore it from the mockup — T096 asserted three
 * tabs before issue #534 added the fourth below, and RPT-2 is still not one of them.
 *
 * **"SOW Extension Report" (issue #534) is a fourth tab, not a replacement for RPT-2.** It answers a
 * different question — every EDJEr with an extension SOW whose start date falls in a chosen range —
 * requested directly by the report's owner rather than drawn from the mockup, and it is gated by the
 * same `CompassReporting` policy as every other report here.
 */
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
 * `router.ts` maps `/reports` to `AvailabilityReportRoute`, so landing on `/compass/reports` puts the
 * Availability Report on screen — but the pathname is not that report's own, so an exact-match active
 * check found nothing and the tab strip highlighted NOTHING while a report was plainly displayed
 * (owner report 2026-08-19). A screen-reader user got no `aria-current` at all.
 *
 * Resolved here rather than by redirecting: a redirect rewrites the address bar out from under someone
 * who typed `/compass/reports` and adds a history entry so Back appears not to work, which is exactly
 * why `reportsIndexRoute` renders in place. This is the same fix `nav-identity.ts` applies for
 * `/compass/` rendering the Team Directory, in the same shape and for the same reason — the two files
 * now answer this interaction identically instead of one treating it as a defect and the other as an
 * accepted cost.
 *
 * Kept beside {@link TABS} so the two cannot drift: if the index route is ever pointed at a different
 * report, this is the line that has to move with it.
 */
const INDEX_RENDERS = '/reports/availability';

/**
 * The reports shell at `/compass/reports` — page header, tab strip, and the active report below.
 *
 * <h3>Tabs are links, not local state</h3>
 * Each surface has its own addressable URL (FR-023, Q3): a tab strip driven by `useState` would give
 * all four reports one URL, breaking deep links, bookmarks, the browser back button and reload — which
 * is why `reports-availability.critical.spec.ts` deep-links to the tab and reloads on it rather than
 * only clicking through.
 *
 * <h3>Composed from the shared components</h3>
 * `PageHeader` comes from `ui.tsx` (FR-024, Principle X). The strip is {@link TabNav}, which this file
 * used to hold inline — it was extracted when the admin shell became its second consumer (owner
 * request 2026-08-21). Everything the strip decides, including the `INDEX_RENDERS` substitution and
 * the AA departure from the mockup's inactive-tab colour, is recorded there.
 */
export function ReportsLayout() {
  return (
    // The same container every other Compass screen uses -- ten of them carry exactly this class run.
    // Without it the report was the ONLY screen with no max width and no padding, so its tables ran the
    // full width of the viewport (visibly wrong at 1920px) while the mockup constrains `.page`. Compass
    // uses max-w-5xl rather than the mockup's literal 1180px; that divergence is pre-existing and
    // recorded in `006` Finding 6, so this follows the house convention rather than reopening it.
    <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
      {/* Title only. The mockup's `.note` beside this heading ("on-screen only for MVP -- PDF export is
          a future enhancement (RPT-1)") is an ANNOTATION, not UI copy: it documents AC-NFR-6 for a
          reader of the mockup, and spec Finding 4 resolves RPT-1 as exactly that rather than as a screen
          element. `ui.tsx`'s own remarks say annotation chips are deliberately not reproduced. The
          requirement it described is met by there being no export control, which T097 asserts. */}
      <PageHeader title="Reports" />

      <TabNav label="Reports" items={TABS} indexPath={REPORTS_INDEX} indexRenders={INDEX_RENDERS} />

      <Outlet />
    </main>
  );
}
