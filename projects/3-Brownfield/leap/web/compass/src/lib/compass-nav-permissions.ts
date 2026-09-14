/**
 * Stable identifier for a top-level Compass navigation destination. Used by
 * `getVisibleCompassNavKeys` to compute which links a user sees based on their Compass
 * privileges (sourced from `GET /api/me` via `useCurrentUser`), and by `CompassNav` to
 * render the matching items.
 */
export type CompassNavKey =
  'team-directory' | 'client-directory' | 'sales-dashboard' | 'reports' | 'admin-config';

/** Visible to any authenticated user — the implicit baseline EDJEr tier (FR-004), not gated on any Compass role. */
const BASELINE_KEYS: CompassNavKey[] = ['team-directory', 'client-directory'];

const SALES_ROLES = ['Compass Sales', 'Compass Ops', 'Compass Super Admin'];
const SUPER_ADMIN_ROLES = ['Compass Super Admin'];
const OPS_ROLES = ['Compass Ops', 'Compass Super Admin'];

/**
 * Filters the mixed `/api/me` privileges array down to Compass-prefixed role strings only.
 *
 * The same array carries Timesheet/OOTO roles for the same person — Compass owns its roles and
 * inherits nothing, so this filter is how that isolation is enforced structurally rather than
 * merely documented.
 */
export function getCompassPrivileges(privileges: string[] | null | undefined): string[] {
  return (privileges ?? []).filter((privilege) => privilege.startsWith('Compass '));
}

/**
 * Returns the set of Compass navigation keys visible to a user.
 *
 * `isAuthenticated` is passed separately from `compassPrivileges`: the baseline directories are
 * gated on being authenticated at all (FR-004's implicit EDJEr tier), not on holding any Compass
 * role — a person with zero Compass groups still sees them, which is why `privileges.length > 0`
 * is never the right check here.
 */
export function getVisibleCompassNavKeys(
  isAuthenticated: boolean,
  compassPrivileges: string[],
): CompassNavKey[] {
  if (!isAuthenticated) {
    return [];
  }

  const keys: CompassNavKey[] = [...BASELINE_KEYS];
  if (compassPrivileges.some((p) => SALES_ROLES.includes(p))) {
    keys.push('sales-dashboard', 'reports');
  }
  if (compassPrivileges.some((p) => SUPER_ADMIN_ROLES.includes(p))) {
    keys.push('admin-config');
  }
  return keys;
}

/**
 * Whether a viewer may create/edit assignments (feature 006) — Compass Ops or Super Admin, the same
 * set the server's `RolePolicy.CompassOps` policy requires (contract §1). This is a USABILITY
 * affordance only: hiding the "New assignment" action for anyone else is not authorization (AC-44,
 * FR-006) — the server refuses independently, and this check must match its policy, not decide it.
 */
export function canManageCompassAssignments(compassPrivileges: string[]): boolean {
  return compassPrivileges.some((p) => OPS_ROLES.includes(p));
}

/**
 * Whether a viewer may PERMANENTLY DELETE an assignment or a SOW (issue #593) — Compass Super
 * Admin only, never Ops. Narrower than {@link canManageCompassAssignments} deliberately: the owner's
 * explicit direction on the issue was "Only Compass-SuperAdmin should be able to perform this
 * function," matching `RolePolicy.CompassSuperAdmin` on the server. This is a USABILITY affordance
 * only — the server refuses the DELETE independently regardless of what this returns.
 */
export function canDeleteCompassAssignments(compassPrivileges: string[]): boolean {
  return compassPrivileges.some((p) => SUPER_ADMIN_ROLES.includes(p));
}
