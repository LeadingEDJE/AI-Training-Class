/**
 * Pure helpers behind `CompassNav`'s topbar chrome: the active-link underline and the userchip's
 * avatar initials. Separate from `compass-nav-permissions.ts`, which answers which links are visible.
 */

/**
 * Initials for the userchip avatar — first letter of the first name and of the last, uppercased.
 *
 * The label reaching this function is routinely an email address rather than a name, because
 * `CompassNav` falls back to `user.email` whenever the DisplayName claim is blank. So an address is
 * split on its local part's separators instead of being treated as one whitespace token:
 * `avery.quinn@example.com` gives "AQ", not "AV".
 */
export function getInitials(displayName: string): string {
  const tokens = nameTokens(displayName);
  if (tokens.length === 0) {
    return '';
  }
  if (tokens.length === 1) {
    return tokens[0].slice(0, 2).toUpperCase();
  }
  return (tokens[0][0] + tokens[tokens.length - 1][0]).toUpperCase();
}

/**
 * The name-ish tokens in a label, whether it is a real display name or an email address.
 *
 * `>= 0`, not `> 0`: a label that is nothing but a domain (`@leadingedje.com`) has no local part and
 * so yields no tokens, which the caller turns into an empty string. Sent down the whitespace branch
 * instead it is one token and renders the avatar "@L".
 */
function nameTokens(label: string): string[] {
  const trimmed = label.trim();
  const atIndex = trimmed.indexOf('@');

  if (atIndex >= 0) {
    return trimmed
      .slice(0, atIndex)
      .split(/[._-]+/)
      .filter(Boolean);
  }

  return trimmed.split(/\s+/).filter(Boolean);
}

/** The Compass base path — what `router.ts` sets as the router's `basepath`. */
const COMPASS_BASE = '/compass';

/**
 * What the index route actually renders. `router.ts` maps `/` to `TeamDirectoryRoute` (issue #248),
 * so landing on `/compass/` puts the Team Directory on screen under a pathname that is not the Team
 * Directory's own.
 *
 * Kept beside {@link isNavLinkActive} so the two cannot drift: if the index route is ever pointed at
 * a different screen, this is the line that has to move with it.
 */
const INDEX_RENDERS = '/compass/team-directory';

/**
 * The pathname an active-link check should compare against: trailing slash removed, and an index path
 * swapped for the route it actually renders.
 *
 * Shared by the three shells whose bare path renders a child component instead of redirecting to it —
 * `/compass/` renders the Team Directory, `/compass/admin` renders Lookup Administration,
 * `/compass/reports` renders the Availability Report. Without the substitution an exact-match check
 * finds nothing and the navigation highlights NOTHING while a screen is plainly displayed, with no
 * `aria-current` for a screen-reader user either. `length > 1` leaves the root `/` alone rather than
 * normalising it to the empty string.
 *
 * ONE named substitution, never a prefix rule — and it resolves a pathname rather than returning a
 * verdict, because callers differ in that last step and are meant to: {@link isNavLinkActive} matches
 * nested paths, `TabNav` matches exactly.
 */
export function resolveActivePath(
  pathname: string,
  indexPath: string,
  indexRenders: string,
): string {
  const normalized =
    pathname.endsWith('/') && pathname.length > 1 ? pathname.slice(0, -1) : pathname;

  return normalized === indexPath ? indexRenders : normalized;
}

/**
 * Whether a nav link should render as active for the current pathname. `CompassNav` renders plain
 * `<a href>` (full page loads — see its own doc comment for why), so there is no router match state
 * to read; this compares the browser's real pathname instead.
 *
 * Matches the exact link target OR a nested path under it (`/compass/admin/edjers/5` is active for
 * the `/compass/admin` link), but never a merely-similar prefix
 * (`/compass/team-directory-extra` is NOT active for `/compass/team-directory`).
 */
export function isNavLinkActive(pathname: string, linkTo: string): boolean {
  const resolved = resolveActivePath(pathname, COMPASS_BASE, INDEX_RENDERS);

  return resolved === linkTo || resolved.startsWith(`${linkTo}/`);
}
