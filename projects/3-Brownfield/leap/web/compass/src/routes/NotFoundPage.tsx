import { buttonClassName } from '../components/ui-classes';

/**
 * Rendered for any path under `/compass/` the router does not match.
 *
 * Before this feature there was no router, so every unmatched path rendered the index page — the
 * serving layer's SPA history fallback (`try_files $uri /compass/index.html$is_args$args`) delivered
 * the shell and the shell only knew one page. Now that routes exist, an unmatched path is answered
 * honestly instead of silently showing something else.
 *
 * A `CompassNav` link whose area is not routed yet also lands here, on purpose: the link is real and
 * the screen is not built, which is what this page says.
 *
 * **Deliberately no list of which those are.** This comment used to name all five nav destinations as
 * later streams and was wrong about four of them; the replacement named the one remaining and was
 * wrong within the hour, because the Reports screen was in review at the time (#293) and has since
 * merged — every nav destination is routed as of that merge. The set is a moving inventory and any
 * copy of it here rots silently, in both directions: a screen lands, or a new nav item ships ahead of
 * its route. `router.ts` is the only honest answer to "what is routed", and it cannot go stale
 * because it IS the routing.
 *
 * One fact that is stable and worth stating, because it is a design decision rather than a build
 * order: `Assignments` is not a nav destination at all. It was removed along with the standalone
 * `/compass/assignments` route in a1a5a249, and assignments are reached from an EDJEr's or a Client's
 * own record instead (008 O-1/O-2, AC-1).
 */
export function NotFoundPage() {
  return (
    <main className="min-h-dvh px-6 py-12 text-brand-text">
      <div className="mx-auto max-w-2xl">
        <h1 className="text-3xl font-semibold tracking-tight">Page Not Found</h1>
        <p className="mt-2 text-sm text-brand-gray-muted">
          This Compass area does not exist yet. Use the navigation above, or return to the Compass
          home page.
        </p>
        {/* A hand-rolled copy of the secondary button until #336: `font-medium` where the helper is
            `font-semibold` (so it had already drifted past the 2026-08-18 weight change) and `py-2`
            where the helper is `py-1.5`. `inline-block` stays — an `<a>` is inline by default and
            would not render the padding — and `mt-6` is this page's own spacing; both belong to the
            caller, like every other composed call site.

            The `<a href>` is deliberate, not an oversight: a full reload is the right thing when the
            router has already failed to match a route. */}
        <a
          href="/compass/"
          className={`mt-6 inline-block ${buttonClassName({ variant: 'secondary' })}`}
        >
          Back to Compass
        </a>
      </div>
    </main>
  );
}
