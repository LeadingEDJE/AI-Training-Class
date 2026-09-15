/** Initials for the userchip avatar — always derived from the display name, never an email address. */
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

const COMPASS_BASE = '/compass';

/** What the index route actually renders, hardcoded here so a future route change must remember to update it too. */
const INDEX_RENDERS = '/compass/team-directory';

export function resolveActivePath(
  pathname: string,
  indexPath: string,
  indexRenders: string,
): string {
  const normalized =
    pathname.endsWith('/') && pathname.length > 1 ? pathname.slice(0, -1) : pathname;

  return normalized === indexPath ? indexRenders : normalized;
}

export function isNavLinkActive(pathname: string, linkTo: string): boolean {
  const resolved = resolveActivePath(pathname, COMPASS_BASE, INDEX_RENDERS);

  return resolved === linkTo || resolved.startsWith(`${linkTo}/`);
}
