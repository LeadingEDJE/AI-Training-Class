import { Outlet } from '@tanstack/react-router';
import { TabNav } from '../../components/TabNav';
import {
  ADMIN_SECTIONS,
  ADMIN_INDEX_PATH,
  ADMIN_INDEX_RENDERS,
  type AdminSection,
} from './admin-sections';

/**
 * The shell every Compass configuration screen renders inside.
 *
 * **Reaching this screen is not what authorises anything.** The `admin-config` nav key is gated to
 * Compass Super Admin in `compass-nav-permissions.ts`, but that is a usability affordance: every
 * write behind these screens is refused server-side for anything less (AC-44, Principle IV). Do not
 * treat this layout, or the nav gate above it, as the permission model.
 */
export function AdminLayout({ sections = ADMIN_SECTIONS }: { sections?: AdminSection[] }) {
  return (
    // `max-w-[1180px]` and 24px of padding are the mockups' `.page` rule verbatim
    // (`max-width:1180px;margin:0 auto;padding:24px`). It was `max-w-4xl` with `py-10`, i.e. 896px —
    // 284px narrower than every screen in `docs/design/edje-compass-mockups.html` is drawn at, which
    // this shell can least afford: `#s-lookups` is the only TWO-COLUMN screen in the set, and at 896px
    // each card was 415px wide, too narrow for "Invoice Frequency Types" and "+ Add Invoice Frequency
    // Type" to share the heading line. The button wrapped, and the two side-by-side tables then began
    // at different heights — a layout defect with a width cause.
    //
    // The other page shells in this SPA use max-w-4xl/5xl/7xl and `py-8`; `ReportsLayout` already
    // records its own divergence from the literal 1180px as pre-existing. This one is not left at
    // whichever of those it happened to pick.
    <main className="min-h-dvh text-brand-text">
      {/* Padding INSIDE the max-width, as `.page` has it — with it on `main` instead the shell is
          1180 + 48 wide and every card is 12px broader than the design source's. */}
      <div className="mx-auto max-w-[1180px] p-6">
        <h1 className="text-2xl font-semibold tracking-tight">Compass Administration</h1>

        {/* The mockups' `.tabs` strip, shared with the reports shell (owner request 2026-08-21).
            These were tinted pills built on `activeProps` — a different navigation idiom one click
            away from the reports tabs, for the same job of switching between sibling surfaces.

            `activeProps` is also what left this strip unable to highlight anything at
            `/compass/admin`: it marks a link active on the router's own match, and the bare admin path
            matches the index route rather than `/admin/lookups`, so landing there highlighted NOTHING
            while Lookup Administration was on screen. `router.ts` recorded that as an accepted
            cosmetic gap on the grounds that the sub-nav had no equivalent of the reports strip's
            named substitution. It has one now — {@link ADMIN_INDEX_RENDERS} — so the gap is closed
            rather than inherited. */}
        {/* `mt-6` on the strip itself, not on a wrapper div carrying one class. TabNav already owns its
            bottom margin, so splitting the top onto a caller-side element left one component's vertical
            rhythm decided in two places — and `ReportsLayout` renders the same strip with no wrapper at
            all, so the two consumers disagreed. */}
        <TabNav
          className="mt-6"
          label="Configuration areas"
          items={sections}
          indexPath={ADMIN_INDEX_PATH}
          indexRenders={ADMIN_INDEX_RENDERS}
        />

        <Outlet />
      </div>
    </main>
  );
}
