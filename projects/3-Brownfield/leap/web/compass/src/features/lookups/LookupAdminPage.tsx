import { LookupSection } from './LookupSection';
import { PageHeader } from '../../components/ui';

/**
 * Lookup administration — the Compass reference data a Super Admin can change without a code change or
 * a migration (issue #58).
 *
 * Both lookups live on ONE screen because AC-25 and AC-26 describe one surface, and because their rules
 * are identical: a unique name, and an active flag that controls whether the value is offered on new
 * records. Each is rendered by its own {@link LookupSection} bound to its own resource.
 *
 * **This screen is the only Compass configuration surface deliberately outside the audit trail**
 * (FR-008, AC-NFR-3). Nothing here records who changed what, and that is the requirement rather than a
 * gap.
 *
 * Reaching the screen is not what authorises the writes behind it: every route requires the Compass
 * root policy server-side, and a Compass Admin is refused even on the reads (FR-009). The nav gate on
 * `admin-config` is a convenience over that enforcement, never a substitute for it.
 */
export function LookupAdminPage() {
  return (
    <div className="flex flex-col gap-6">
      {/* `Lookup Administration` is the design source's title for `#s-lookups` (FR-011). The ADMIN
          SUB-NAV keeps the shorter "Lookups" — a nav label and a page title are different things, and
          the mockup's own nav is equally terse. Uses PageHeader rather than a hand-rolled header,
          which is why two h1 sizes used to ship.

          NO description, matching the mockup's bare `.pagehead`. It carried one until 2026-08-21
          ("Reference values the rest of Compass classifies records by. Changes take effect
          immediately.") — which restated `AdminLayout`'s own subtitle two lines above it, ending in
          the same five words. Each lookup's own "offered when …" note went the same way (issue #408,
          2026-08-26): it was the mockup's `.note` under the table it applied to, and was removed as
          an owner-requested explanation nobody needed on screen. */}
      <PageHeader title="Lookup Administration" />

      {/* Two columns, per the mockup's `.two-col`, collapsing to one on a narrow viewport.
          `lg:` (1024px) rather than `md:` (768px), which is the breakpoint this project otherwise fixes
          for layout: each card holds a three-column table, and side-by-side at 768px they are cramped
          enough that the tables scroll inside their own wrappers on a device with room to spare. The
          mockup itself collapses below 900px, and `lg` is the nearest step above that without inventing a
          breakpoint. The one-breakpoint rule exists so NAV elements cannot leave a dead zone with nothing
          to navigate by; a content grid has no such failure mode.

          `gap-5` is the mockup's own `.two-col{gap:20px}`. */}
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
        <LookupSection kind="employee-types" title="Employee Types" singular="Employee Type" />

        <LookupSection
          kind="invoice-frequency-types"
          title="Invoice Frequency Types"
          singular="Invoice Frequency Type"
        />
      </div>
    </div>
  );
}
