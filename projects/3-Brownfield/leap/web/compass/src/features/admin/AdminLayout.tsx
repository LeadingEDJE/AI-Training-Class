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
 * The `admin-config` nav key gated to Compass Super Admin in `compass-nav-permissions.ts` is the
 * actual permission boundary here — the write endpoints behind these screens trust that a user who
 * can reach this layout has already been authorized.
 */
export function AdminLayout({ sections = ADMIN_SECTIONS }: { sections?: AdminSection[] }) {
  return (
    <main className="min-h-dvh text-brand-text">
      <div className="mx-auto max-w-[1180px] p-6">
        <h1 className="text-2xl font-semibold tracking-tight">Compass Administration</h1>

        {/* The mockups' `.tabs` strip, shared with the reports shell. Built on `activeProps`, the
            same navigation idiom the reports tabs use.

            This still cannot highlight anything at `/compass/admin` — {@link ADMIN_INDEX_RENDERS}
            only affects which panel renders under the strip, not which tab lights up, so the known
            highlight gap remains open even though a fix landed for the render side. */}
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
