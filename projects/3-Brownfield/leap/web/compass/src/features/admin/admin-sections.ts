import type { TabNavItem } from '../../components/TabNav';

/**
 * A configuration area reachable from the admin shell.
 *
 * **An alias of {@link TabNavItem}, not a second declaration of the same shape.** The admin strip IS
 * a `TabNav`, so the tab strip owns the contract and this name records what one of its items means
 * here. Declaring the two separately compiled only because TypeScript is structural, and left a
 * future change to one silently diverging from the other.
 *
 * That also buys the typing: `to` was `string`, so a section pointing at a route the router does not
 * serve was unreachable from the shell with nothing to catch it. It is now checked against the route
 * tree at build time.
 *
 * `to` omits the `/compass` prefix — the router carries that as its `basepath`, so repeating it here
 * would resolve to `/compass/compass/...`. `CompassNav` uses prefixed `<a href>` values instead,
 * because it is a plain link shell that predates the router.
 */
export type AdminSection = TabNavItem;

/**
 * Every configuration area the admin shell offers, in display order.
 *
 * Areas are added here as they are built. A section is listed only once its route exists; a link to a
 * route that resolves to the not-found page would be worse than no link.
 *
 * Each entry is also named individually in `AdminLayout.test.tsx`. The test that iterates this array
 * guards the injection default and adapts to whatever the array holds, which means it cannot catch the
 * opposite mistake: a screen whose route exists but whose entry was forgotten is unreachable from the
 * shell, and an iterating test passes regardless.
 *
 * This lives apart from `AdminLayout.tsx` because `react-refresh/only-export-components` requires a
 * component module to export components only.
 */
export const ADMIN_SECTIONS: AdminSection[] = [
  { to: '/admin/lookups', label: 'Lookups' },
  { to: '/admin/edjers', label: 'EDJErs' },
  { to: '/admin/clients', label: 'Clients' },
];

/**
 * Which section `/compass/admin` actually renders.
 *
 * `router.ts`'s `adminIndexRoute` maps the bare admin path to `LookupAdminPage` rather than
 * redirecting to `/admin/lookups`, so the pathname is not that section's own and an exact-match
 * active check finds nothing. `TabNav` takes this as its one named substitution.
 *
 * Kept beside {@link ADMIN_SECTIONS} so the two cannot drift: if the index route is ever pointed at a
 * different section, this is the line that has to move with it — the same pairing `ReportsLayout`
 * keeps between its own tabs and `INDEX_RENDERS`.
 */
export const ADMIN_INDEX_RENDERS = '/admin/lookups';

/**
 * The admin shell's own path — what `TabNav` substitutes {@link ADMIN_INDEX_RENDERS} for.
 *
 * A named constant BESIDE its partner, which is the whole point: `ADMIN_INDEX_RENDERS`' doc promised
 * the pair could not drift, while this half sat inline in `AdminLayout`'s JSX with nothing linking
 * the two. Moving the shell would have meant editing a literal that no reader of this file could see,
 * and missing it highlights nothing at the index path — the exact defect the pair exists to fix.
 * `ReportsLayout` keeps both halves as constants for the same reason.
 */
export const ADMIN_INDEX_PATH = '/admin';
