import { Link, useRouterState } from '@tanstack/react-router';
import type { LinkProps } from '@tanstack/react-router';
import { resolveActivePath } from '../lib/nav-identity';

/**
 * A path the registered router actually serves.
 *
 * **Not `string`.** `ReportsLayout` used to declare its tabs `as const` and hand the literals straight
 * to `<Link to>`, which typechecks them against the route tree — a mistyped report path was a compile
 * error that even suggested the correction. Extracting the strip behind a `to: string` prop silently
 * threw that away: the widened type accepts anything, and a typo ships a tab that routes to
 * not-found. Borrowing `Link`'s own `to` type restores the check at every call site.
 */
type RoutePath = LinkProps['to'];

/** One destination in a {@link TabNav}. */
export interface TabNavItem {
  /**
   * A router path WITHOUT the `/compass` prefix — the router carries that as its `basepath`, so
   * repeating it here resolves to `/compass/compass/...`.
   *
   * Typed as {@link RoutePath}, so a path the router does not serve fails the build.
   */
  to: RoutePath;
  label: string;
}

interface TabNavProps {
  /** The landmark's accessible name, e.g. "Reports" or "Configuration areas". */
  label: string;
  items: TabNavItem[];
  /**
   * The shell's own index path — `/reports`, `/admin`.
   *
   * Paired with {@link TabNavProps.indexRenders}; see that field for what the pair is for.
   */
  indexPath: string;
  /**
   * Which item the index path actually renders.
   *
   * Both Compass shells map their bare path to a child component rather than redirecting to it, so
   * landing on `/compass/admin` puts Lookup Administration on screen while the pathname is not
   * Lookups' own. An exact-match active check therefore finds nothing and the strip highlights
   * NOTHING while a screen is plainly displayed — and a screen-reader user gets no `aria-current` at
   * all. This is the one named substitution that resolves it.
   *
   * Deliberately NOT a prefix rule — see the note on `isActive` below.
   */
  indexRenders: string;
  /**
   * Extra classes for the strip's own element — in practice its outer spacing.
   *
   * The strip owns its bottom margin because that gap is part of looking like a tab strip; its TOP
   * margin depends on what precedes it, which only the caller knows. `ReportsLayout` follows a
   * `PageHeader` and needs none; the admin shell follows a paragraph and needs `mt-6`.
   */
  className?: string;
}

/**
 * The mockups' `.tabs` strip: a row of navigation links, the current one underlined in EDJE green.
 *
 * <h3>Extracted from `ReportsLayout` when the admin shell became the second consumer</h3>
 * Two occurrences, not three — normally the Rule of Three says wait. It does not apply here, because
 * the second consumer exists in response to "make the admin nav work like the reports nav" (owner
 * request 2026-08-21). That instruction makes them ONE concept by definition rather than two that
 * happen to look alike, so the abstraction is not speculative.
 *
 * The stronger reason is what a hand-copy would have had to get right: exact-equality matching,
 * `aria-current`, the AA colour departure, and the pathname resolution. Every one is a correctness
 * detail whose failure is silent.
 *
 * The pathname resolution is no longer duplicated here at all: it was a verbatim copy of two lines in
 * `nav-identity.ts` — which this comment previously cited as precedent while reproducing it — and now
 * calls {@link resolveActivePath}, the one place all three Compass shells resolve an index path.
 *
 * <h3>Links, not the ARIA tab pattern</h3>
 * These navigate, so the accessible pattern is a set of navigation links inside a labelled landmark —
 * not `role="tab"`, which expects tabpanels a widget shows and hides. `aria-current="page"` announces
 * the active one. Each destination keeps its own addressable URL, so deep links, bookmarks, Back and
 * reload all work; a strip driven by `useState` would give every destination one URL.
 *
 * <h3>One departure from the mockup's colours, for AA</h3>
 * The mockup styles an inactive tab `var(--muted)` = `#7A7C7F`, which is 4.20:1 on white and fails the
 * 4.5:1 normal-text minimum at the 13px it uses. Accessibility outranks fidelity (Principle X rule 2),
 * so inactive tabs take `brand-gray-muted` (`#545A60`), the existing darkened step that clears AA on
 * both white and the `#f4f5f6` shell. No new token. Everything else follows the mockup: 13px, the 3px
 * transparent bottom border that becomes EDJE green when active, and the active label at semibold.
 */
export function TabNav({ label, items, indexPath, indexRenders, className = '' }: TabNavProps) {
  // The router's own state, not a local `activeTab`: the URL is the single source of truth for which
  // destination is showing, so a deep link and a click produce identically the same highlighted tab.
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  // Trailing slash and index substitution, shared with `CompassNav`'s own active-link check — this used
  // to be two lines copied here from `ReportsLayout`, which had them copied from `nav-identity.ts`.
  const resolved = resolveActivePath(pathname, indexPath, indexRenders);

  return (
    // Rendered even when `items` is empty, so the layout does not shift as destinations are added —
    // the same reasoning CompassNav applies to rendering its own <nav> with zero links.
    <nav
      aria-label={label}
      className={`mb-4 flex flex-wrap gap-0.5 border-b-2 border-brand-taupe/40 ${className}`}
    >
      {items.map((item) => {
        // EXACT equality, not `startsWith`. A prefix test marks two tabs active as soon as one path
        // extends another's — a sibling like `/admin/edjers-archive`, or a child like
        // `/admin/edjers/new` — which paints two underlines and announces two current pages. Both
        // shells already have such children: `/admin/edjers/new` and `/admin/clients/$clientId` are
        // real routes today, so the loose form is a live defect rather than a hypothetical. A child
        // route that should light its parent tab must say so explicitly, the way `indexRenders` does:
        // one named substitution, never a prefix rule.
        const isActive = resolved === item.to;

        return (
          <Link
            key={item.to}
            to={item.to}
            aria-current={isActive ? 'page' : undefined}
            // `-mb-0.5` pulls the 3px border over the strip's own 2px bottom border so the active
            // underline replaces it rather than sitting below it — the mockup's `margin-bottom:-2px`.
            className={`-mb-0.5 border-b-[3px] px-[18px] py-[9px] text-[13px] ${
              isActive
                ? 'border-brand-green font-semibold text-brand-text'
                : 'border-transparent text-brand-gray-muted hover:text-brand-text'
            }`}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
